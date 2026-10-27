namespace TransactionJournal.Tests.Hints.Engine;

using System.Diagnostics;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Трассировка прохода подсказок на фейковом приёмнике ActivityListener:
/// корневой спан hint_pass с атрибутами исхода и счётчиков, дочерние спаны
/// фаз по порядку исполнения. Проход детерминирован и модель не вызывает —
/// атрибутов gen_ai.* на спанах прохода нет.
// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
/// </summary>
[TestClass]
public class HintPassTracingTests
{
	/// <summary>Имя источника спанов прохода — контракт sink-подписки.</summary>
	private const string SourceName = "TransactionJournal.Hints";

	/// <summary>Имена шести фаз прохода в порядке исполнения.</summary>
	private static readonly string[] PhaseNames =
	[
		"hint_pass.corpus_snapshot",
		"hint_pass.expire_withdrawn_rules",
		"hint_pass.journal_snapshot",
		"hint_pass.expire_closed_subjects",
		"hint_pass.quotes",
		"hint_pass.evaluate_triggers",
	];

	[TestMethod]
	[Description("Завершённый проход пишет корень hint_pass с исходом, счётчиками и шестью дочерними фазами")]
	// Трасса прохода: корень hint_pass без родителя, затем шесть фаз в порядке
	// исполнения с корнем в родителе; исход и счётчики проставлены на корне,
	// итог прохода несёт trace id корневого спана.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-hint-pass-phases
	public async Task TryIfPassCompletes_TraceHasRootWithOutcomeAndSixPhases()
	{
		var dir = CorpusYaml.TempDir();
		using var listener = new HintTracingListener(SourceName);
		try
		{
			// Arrange: корпус с одним срабатывающим правилом цели прибыли.
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				clarity: "fuzzy",
				hintTemplate: "Результат конструкции {pnlPct}% — плановая прибыль достигнута.",
				character: "profit-target"));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(
						id: 7,
						capital: 1000m,
						profitValue: 10m,
						profitUnit: TargetUnit.Percent,
						totalPnL: 120m),
				],
			};

			// Act.
			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			// Assert: проход создал подсказку — на корне счётчик созданных.
			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(added, Has.Count.EqualTo(1));
			var root = AssertRootSpan(listener);

			// Атрибуты исхода и счётчиков результата — на корневом спане.
			Assert.That(root.GetTagItem("tj.hints.outcome"), Is.EqualTo("completed"));
			Assert.That(root.GetTagItem("tj.hints.created"), Is.EqualTo(1));
			Assert.That(root.GetTagItem("tj.hints.expired"), Is.EqualTo(0));

			// Дерево: после корня — ровно шесть фаз в порядке исполнения, у
			// каждой родитель — корневой спан, все внутреннего вида.
			Assert.That(listener.Started, Has.Count.EqualTo(7));
			var phases = listener.Started.Skip(1).ToList();
			Assert.That(phases.Select(phase => phase.OperationName).ToArray(), Is.EqualTo(PhaseNames));
			foreach (var phase in phases)
			{
				Assert.That(phase.Kind, Is.EqualTo(ActivityKind.Internal));
				Assert.That(phase.Parent, Is.SameAs(root));
			}

			// Сетевая задержка котировок видна отдельным спаном фазы.
			Assert.That(phases.Single(phase => phase.OperationName == "hint_pass.quotes"), Is.Not.Null);

			// Проход без LLM: атрибутов gen_ai.* нет ни на одном спане.
			foreach (var activity in listener.Started)
			{
				Assert.That(activity.Tags.Any(tag => tag.Key.StartsWith("gen_ai.", StringComparison.Ordinal)), Is.False);
			}

			// Итог прохода несёт trace id корневого спана — 32 hex.
			Assert.That(result.TraceId, Is.EqualTo(root.TraceId.ToHexString()));
			Assert.That(result.TraceId, Does.Match("^[0-9a-f]{32}$"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Пропуск из-за котировок закрывает корневой спан с исходом skipped-market-unavailable и счётчиком гашений")]
	// Ранний возврат market-unavailable тоже внутри корневого спана: исход и
	// счётчики проставлены до завершения, фаза оценки триггеров не открывается.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-hint-pass-phases
	public async Task TryIfMarketUnavailable_RootCarriesOutcomeAndCounters()
	{
		var dir = CorpusYaml.TempDir();
		using var listener = new HintTracingListener(SourceName);
		try
		{
			// Arrange: валидный корпус, недоступный источник котировок.
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card("tc-target", implementation: "profit-target-reached"));

			// Act.
			var (result, added) = await HintPassHarness.RunAsync(
				dir,
				new JournalSnapshot { Constructions = [] },
				unavailableReason: "биржа недоступна");

			// Assert: исход пропуска и счётчики на корне, фаз до котировок — пять.
			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.SkippedMarketUnavailable));
			Assert.That(added, Is.Empty);
			var root = AssertRootSpan(listener);
			Assert.That(root.GetTagItem("tj.hints.outcome"), Is.EqualTo("skipped-market-unavailable"));
			Assert.That(root.GetTagItem("tj.hints.created"), Is.EqualTo(0));
			Assert.That(root.GetTagItem("tj.hints.expired"), Is.EqualTo(0));
			Assert.That(listener.Started, Has.Count.EqualTo(6));
			Assert.That(
				listener.Started.Skip(1).Select(phase => phase.OperationName).ToArray(),
				Is.EqualTo(PhaseNames.Take(5).ToArray()));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Сломанный корпус закрывает корневой спан с исходом corpus-invalid до чтений журнала")]
	// Ранний возврат corpus-invalid внутри корневого спана: исход проставлен,
	// счётчики нулевые, других фаз прохода после снимка корпуса нет.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-hint-pass-phases
	public async Task TryIfCorpusInvalid_RootCarriesOutcomeWithoutFurtherPhases()
	{
		var dir = CorpusYaml.TempDir();
		using var listener = new HintTracingListener(SourceName);
		try
		{
			// Arrange: файл, не парсящийся как YAML, — битый корпус.
			CorpusYaml.Write(dir, "broken.yaml", "::: не yaml");

			// Act.
			var (result, added) = await HintPassHarness.RunAsync(dir, new JournalSnapshot { Constructions = [] });

			// Assert: исход corpus-invalid на корне, фаза снимка корпуса — единственная.
			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.CorpusInvalid));
			Assert.That(result.Diagnostics, Is.Not.Empty);
			Assert.That(added, Is.Empty);
			var root = AssertRootSpan(listener);
			Assert.That(root.GetTagItem("tj.hints.outcome"), Is.EqualTo("corpus-invalid"));
			Assert.That(root.GetTagItem("tj.hints.created"), Is.EqualTo(0));
			Assert.That(root.GetTagItem("tj.hints.expired"), Is.EqualTo(0));
			Assert.That(listener.Started, Has.Count.EqualTo(2));
			Assert.That(listener.Started[1].OperationName, Is.EqualTo("hint_pass.corpus_snapshot"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	/// <summary>Проверяет и возвращает корневой спан прохода: первый старт, источник прохода, без родителя.</summary>
	private static Activity AssertRootSpan(HintTracingListener listener)
	{
		Assert.That(listener.Started, Is.Not.Empty);
		var root = listener.Started[0];
		Assert.That(root.OperationName, Is.EqualTo("hint_pass"));
		Assert.That(root.Source.Name, Is.EqualTo(SourceName));
		Assert.That(root.Parent, Is.Null);
		Assert.That(root.Kind, Is.EqualTo(ActivityKind.Internal));
		return root;
	}
}
