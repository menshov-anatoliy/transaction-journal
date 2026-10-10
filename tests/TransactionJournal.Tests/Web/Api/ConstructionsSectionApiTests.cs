namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Api;
using TransactionJournal.Application;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Application.Sync;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки API раздела «Конструкции»: список со сводкой,
/// превью конструкции, панель подсказок журнала, ручной проход агента и
/// кнопка синхронизации. Доменные read-модели подменяются стабильными
/// заглушками — проверяется HTTP-контракт раздела, а не повторное
/// вычисление метрик (метрики покрыты собственными тестами read-моделей).
/// </summary>
[TestClass]
public sealed class ConstructionsSectionApiTests
{
	[TestMethod]
	[Description("Список конструкций отдаёт сводку журнала и строки таблицы одним ответом")]
	// Раздел «Конструкции» получает данные через единый версионированный API:
	// сводка (итог, счётчики) и строки таблицы приходят одним запросом, чтобы
	// шапка и таблица не расходились между перезагрузками.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfOverviewServesSummaryAndRows()
	{
		// Arrange: стабильная read-модель с одной открытой конструкцией.
		await using var factory = new SectionApiFactory(services => services.ReplaceReadModel(new StubListReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос обзора раздела.
		var response = await client.GetAsync("/api/v1/constructions");

		// Assert: успешный JSON со сводкой журнала и строкой конструкции.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		var summary = payload!["summary"]!;
		Assert.That(summary["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(100.5m));
		Assert.That(summary["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(60.25m));
		Assert.That(summary["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(40.25m));
		Assert.That(summary["marksAsOf"]!.GetValue<string>(), Is.EqualTo("2026-06-20T14:30:00+00:00"));
		Assert.That(summary["hasMarkFailure"]!.GetValue<bool>(), Is.False);
		Assert.That(summary["constructionCount"]!.GetValue<int>(), Is.EqualTo(2));
		Assert.That(summary["openCount"]!.GetValue<int>(), Is.EqualTo(1));

		var row = payload["items"]!.AsArray().Single(node => node!["constructionId"]!.GetValue<long>() == 7L);
		Assert.That(row!["name"]!.GetValue<string>(), Is.EqualTo("ETH-240628-3200C+P"));
		Assert.That(row["status"]!.GetValue<string>(), Is.EqualTo("open"));
		Assert.That(row["allocatedCapitalUsdt"]!.GetValue<decimal>(), Is.EqualTo(3000m));
		Assert.That(row["riskUsdt"]!.GetValue<decimal>(), Is.EqualTo(90m));
		Assert.That(row["profitUsdt"]!.GetValue<decimal>(), Is.EqualTo(240m));
		Assert.That(row["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(60.25m));
		Assert.That(row["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(40.25m));
		Assert.That(row["adjustmentsPnL"]!.GetValue<decimal>(), Is.EqualTo(0m));
		Assert.That(row["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(100.5m));
		Assert.That(row["totalPnLPercent"]!.GetValue<decimal>(), Is.EqualTo(3.35m));
		Assert.That(row["markValue"]!.GetValue<decimal>(), Is.EqualTo(500m));
		Assert.That(row["capitalUsagePercent"]!.GetValue<decimal>(), Is.EqualTo(16.7m));
		Assert.That(row["realRiskUsdt"]!.GetValue<decimal>(), Is.EqualTo(150m));
		Assert.That(row["openedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-18T09:05:00+00:00"));
		Assert.That(row["closedAt"], Is.Null);
	}

	[TestMethod]
	[Description("Сбой котировок гасит нереализованные величины сводки и строк в null")]
	// Деградация при сбое котировок — сквозной паттерн величин: недоступная
	// нереализованная часть и итог передаются null, а не нулём, чтобы SPA
	// показывал «неполный»/«сбой котировок» вместо ложного нуля.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	public async Task TryIfOverviewDegradesUnrealizedOnMarkFailure()
	{
		// Arrange: read-модель со сбоем марок — итог и нереализованные части null.
		await using var factory = new SectionApiFactory(services => services.ReplaceReadModel(StubListReadModel.WithMarkFailure()));
		using var client = factory.CreateClient();

		// Act: запрос обзора раздела.
		var payload = await (await client.GetAsync("/api/v1/constructions")).Content.ReadFromJsonAsync<JsonNode>();

		// Assert: сводка и строка несут null вместо величин и признак сбоя.
		Assert.That(payload!["summary"]!["hasMarkFailure"]!.GetValue<bool>(), Is.True);
		Assert.That(payload["summary"]!["totalPnL"], Is.Null);
		Assert.That(payload["summary"]!["unrealizedPnL"], Is.Null);
		Assert.That(payload["summary"]!["marksAsOf"], Is.Null);
		var row = payload["items"]!.AsArray().Single();
		Assert.That(row!["unrealizedPnL"], Is.Null);
		Assert.That(row["totalPnL"], Is.Null);
		Assert.That(row["markValue"], Is.Null);
		Assert.That(row["capitalUsagePercent"], Is.Null);
		// Реальный риск от марок не зависит: при сбое котировок он остаётся
		// доступным, пока нереализованные величины гаснут в null.
		// Traceability: openspec:analytics/performance#scenario-real-risk-marks-failure-independent
		Assert.That(row["realRiskUsdt"]!.GetValue<decimal>(), Is.EqualTo(150m));
		// Реализованная часть и корректировки при сбое марок остаются видимыми.
		Assert.That(row["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(60.25m));
		Assert.That(row["adjustmentsPnL"]!.GetValue<decimal>(), Is.EqualTo(0m));
	}

	[TestMethod]
	[Description("Строки списка несут счётчик живых подсказок для бейджа строки")]
	// Бейдж живых подсказок в строке таблицы: список конструкций соединяется
	// со счётчиками живых записей подсказок — у конструкции без живых
	// подсказок счётчик нулевой, бейдж не рисуется.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	public async Task TryIfOverviewJoinsLiveHintCounts()
	{
		// Arrange: у конструкции 7 — две живые подсказки, у 8 — ни одной.
		await using var factory = new SectionApiFactory(services => services
			.ReplaceReadModel(new StubListReadModel())
			.ReplaceHintDisplay(new StubHintDisplayReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос обзора раздела.
		var payload = await (await client.GetAsync("/api/v1/constructions")).Content.ReadFromJsonAsync<JsonNode>();

		// Assert: счётчики живых подсказок приходят в строках списка.
		var withHints = payload!["items"]!.AsArray().Single(node => node!["constructionId"]!.GetValue<long>() == 7L);
		var withoutHints = payload["items"]!.AsArray().Single(node => node!["constructionId"]!.GetValue<long>() == 8L);
		Assert.That(withHints!["liveHintCount"]!.GetValue<int>(), Is.EqualTo(2));
		Assert.That(withoutHints!["liveHintCount"]!.GetValue<int>(), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Превью конструкции отдаёт метрики, период и счётчики записей")]
	// Правая область при выделенной конструкции показывает read-only превью:
	// сводку метрик, полный индикатор финрезультата (риск/профит), период и
	// счётчики позиций/сделок/корректировок — один запрос превью.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfPreviewServesMetricsPeriodAndCounts()
	{
		// Arrange: стабильная read-модель деталей конструкции 7.
		await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(new StubDetailReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос превью конструкции.
		var response = await client.GetAsync($"/api/v1/constructions/{StubListReadModel.OpenConstructionId}/preview");

		// Assert: успешный JSON с данными превью.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["constructionId"]!.GetValue<long>(), Is.EqualTo(7L));
		Assert.That(payload["name"]!.GetValue<string>(), Is.EqualTo("ETH-240628-3200C+P"));
		Assert.That(payload["status"]!.GetValue<string>(), Is.EqualTo("open"));
		Assert.That(payload["allocatedCapitalUsdt"]!.GetValue<decimal>(), Is.EqualTo(3000m));
		Assert.That(payload["riskUsdt"]!.GetValue<decimal>(), Is.EqualTo(90m));
		Assert.That(payload["profitUsdt"]!.GetValue<decimal>(), Is.EqualTo(240m));
		Assert.That(payload["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(60.25m));
		Assert.That(payload["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(40.25m));
		Assert.That(payload["adjustmentsPnL"]!.GetValue<decimal>(), Is.EqualTo(5m));
		Assert.That(payload["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(105.5m));
		Assert.That(payload["totalPnLPercent"]!.GetValue<decimal>(), Is.EqualTo(3.5m));
		Assert.That(payload["markValue"]!.GetValue<decimal>(), Is.EqualTo(500m));
		Assert.That(payload["capitalUsagePercent"]!.GetValue<decimal>(), Is.EqualTo(16.7m));
		Assert.That(payload["realRiskUsdt"]!.GetValue<decimal>(), Is.EqualTo(150m));
		Assert.That(payload["openedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-18T09:05:00+00:00"));
		Assert.That(payload["closedAt"], Is.Null);
		Assert.That(payload["marksAsOf"]!.GetValue<string>(), Is.EqualTo("2026-06-20T14:30:00+00:00"));
		Assert.That(payload["hasMarkFailure"]!.GetValue<bool>(), Is.False);
		Assert.That(payload["counts"]!["positions"]!.GetValue<int>(), Is.EqualTo(2));
		Assert.That(payload["counts"]!["openPositions"]!.GetValue<int>(), Is.EqualTo(1));
		Assert.That(payload["counts"]!["trades"]!.GetValue<int>(), Is.EqualTo(3));
		Assert.That(payload["counts"]!["adjustments"]!.GetValue<int>(), Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Превью неизвестной конструкции отвечает 404")]
	// Превью выделенной строки — read-only: чужой или удалённый идентификатор
	// даёт явный 404 вместо пустого превью, SPA снимает выделение.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	public async Task ThrowOnUnknownConstructionPreviewReturns404()
	{
		// Arrange: стабильная read-модель, знающая только конструкцию 7.
		await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(new StubDetailReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос превью неизвестной конструкции.
		var response = await client.GetAsync("/api/v1/constructions/999/preview");

		// Assert: эндпоинт отвечает 404.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[TestMethod]
	[Description("Панель подсказок журнала отдаёт группы справочника, историю и счётчик")]
	// Правая область без выделения — панель подсказок журнала: живые подсказки
	// группами справочника v1, свёрнутая терминальная история и полный состав
	// карточек — одним запросом для панели.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfJournalHintsPanelServesGroupsAndHistory()
	{
		// Arrange: стабильная read-модель подсказок с одной живой записью и одной историей.
		await using var factory = new SectionApiFactory(services => services.ReplaceHintDisplay(new StubHintDisplayReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос панели субъекта «журнал».
		var response = await client.GetAsync("/api/v1/hints/panel?subject=journal");

		// Assert: успешный JSON с группами, карточкой полного состава и историей.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["subject"]!["kind"]!.GetValue<string>(), Is.EqualTo("journal"));
		Assert.That(payload["subject"]!["constructionId"], Is.Null);
		Assert.That(payload["liveCount"]!.GetValue<int>(), Is.EqualTo(1));

		var group = payload["liveGroups"]!.AsArray().Single()!;
		Assert.That(group["group"]!["id"]!.GetValue<string>(), Is.EqualTo("risk-mode"));
		Assert.That(group["group"]!["title"]!.GetValue<string>(), Is.EqualTo("Риск-режим"));

		var hint = group["hints"]!.AsArray().Single()!;
		Assert.That(hint["id"]!.GetValue<long>(), Is.EqualTo(5L));
		Assert.That(hint["ruleId"]!.GetValue<string>(), Is.EqualTo("risk-limit-week"));
		Assert.That(hint["character"]!.GetValue<string>(), Is.EqualTo("risk-mode"));
		Assert.That(hint["clarity"]!.GetValue<string>(), Is.EqualTo("crisp"));
		Assert.That(hint["status"]!.GetValue<string>(), Is.EqualTo("new"));
		Assert.That(hint["firstSeenAt"], Is.Null);
		Assert.That(hint["text"]!.GetValue<string>(), Does.Contain("Лимит риска недели"));
		Assert.That(hint["facts"]!["weekPnl"]!.GetValue<string>(), Is.EqualTo("2.1%"));
		var source = hint["sources"]!.AsArray().Single()!;
		Assert.That(source["tag"]!.GetValue<string>(), Is.EqualTo("ПИ"));
		Assert.That(source["quotes"]!.AsArray(), Has.Count.EqualTo(1));

		var historyEntry = payload["history"]!.AsArray().Single()!;
		Assert.That(historyEntry["status"]!.GetValue<string>(), Is.EqualTo("applied"));
	}

	[TestMethod]
	[Description("Панель подсказок конструкции адресуется субъектом конструкции")]
	// Превью выделенной конструкции показывает живые подсказки конструкции:
	// та же панель, адресованная субъектом construction с идентификатором.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	public async Task TryIfConstructionHintsPanelServesSubject()
	{
		// Arrange: стабильная read-модель подсказок.
		await using var factory = new SectionApiFactory(services => services.ReplaceHintDisplay(new StubHintDisplayReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос панели субъекта «конструкция 7».
		var response = await client.GetAsync($"/api/v1/hints/panel?subject=construction&constructionId={StubListReadModel.OpenConstructionId}");

		// Assert: панель несёт субъект конструкции.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["subject"]!["kind"]!.GetValue<string>(), Is.EqualTo("construction"));
		Assert.That(payload["subject"]!["constructionId"]!.GetValue<long>(), Is.EqualTo(7L));
	}

	[TestMethod]
	[Description("Панель подсказок без субъекта отвечает 400")]
	// Субъект панели обязателен: запрос без параметра — ошибка контракта,
	// а не панель по умолчанию — правая область явно выбирает субъект.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	public async Task ThrowOnHintsPanelWithoutSubjectReturns400()
	{
		// Arrange: стабильная read-модель подсказок.
		await using var factory = new SectionApiFactory(services => services.ReplaceHintDisplay(new StubHintDisplayReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос панели без параметров субъекта.
		var response = await client.GetAsync("/api/v1/hints/panel");

		// Assert: эндпоинт отвечает 400.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
	}

	[TestMethod]
	[Description("Ручной запуск прохода отдаёт исход со счётчиками и диагностикой")]
	// Кнопка «Запустить проход подсказок» выполняет проход агента через API:
	// исход (включая corpus-invalid со списком проблем), счётчики созданных и
	// погашенных записей возвращаются ответом команды.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	[DataRow("completed")]
	[DataRow("corpus-invalid")]
	public async Task TryIfHintsPassReturnsOutcomeCountersAndDiagnostics(string outcome)
	{
		// Arrange: стабильный раннер прохода с исходом из DataRow.
		var result = outcome == "completed"
			? StubHintPassRunner.Completed()
			: StubHintPassRunner.CorpusInvalid();
		await using var factory = new SectionApiFactory(services => services.ReplaceHintPassRunner(new StubHintPassRunner(result)));
		using var client = factory.CreateClient();

		// Act: команда ручного запуска прохода.
		var response = await client.PostAsync("/api/v1/hints/pass", content: null);

		// Assert: исход прохода приходит ответом команды.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["outcome"]!.GetValue<string>(), Is.EqualTo(outcome));
		if (outcome == "completed")
		{
			Assert.That(payload["createdHints"]!.GetValue<int>(), Is.EqualTo(2));
			Assert.That(payload["expiredHints"]!.GetValue<int>(), Is.EqualTo(1));
		}
		else
		{
			// CorpusInvalid несёт список проблем корпуса для показа владельцу.
			Assert.That(payload["diagnostics"]!.AsArray(), Has.Count.EqualTo(1));
			Assert.That(payload["diagnostics"]!.AsArray().Single()!.GetValue<string>(), Does.Contain("risk-limit-week"));
		}
	}

	[TestMethod]
	[Description("Команды панели переводят подсказку и возвращают факт перехода")]
	// Кнопки «Применено»/«Отклонено» — единственные мутации подсказок в UI:
	// команда возвращает факт перехода, повтор по терминальной записи — false.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	public async Task TryIfApplyDismissAndSeenTransitionHints()
	{
		// Arrange: read-модель подсказок с живой записью 5.
		var hintDisplays = new StubHintDisplayReadModel();
		await using var factory = new SectionApiFactory(services => services.ReplaceHintDisplay(hintDisplays));
		using var client = factory.CreateClient();

		// Act: команда «Применено» по живой записи.
		var applied = await (await client.PostAsync("/api/v1/hints/5/apply", content: null)).Content.ReadFromJsonAsync<JsonNode>();

		// Assert: переход выполнен.
		Assert.That(applied!["transitioned"]!.GetValue<bool>(), Is.True);
		Assert.That(hintDisplays.AppliedHintIds, Is.EqualTo(new[] { 5L }));

		// Act: повторная команда по уже терминальной записи.
		var reapplied = await (await client.PostAsync("/api/v1/hints/5/apply", content: null)).Content.ReadFromJsonAsync<JsonNode>();

		// Assert: повторный переход не выполнен — терминальные статусы reopen не имеют.
		Assert.That(reapplied!["transitioned"]!.GetValue<bool>(), Is.False);

		// Act: автопометка первого показа.
		var seenResponse = await client.PostAsync("/api/v1/hints/6/seen", content: null);

		// Assert: пометка принята, момент ставит сервер.
		Assert.That(seenResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
		Assert.That(hintDisplays.SeenHintIds, Is.EqualTo(new[] { 6L }));
	}

	[TestMethod]
	[Description("Журнал подсказок отдаёт записи всех субъектов с фильтрами и пагинацией")]
	// Раздел «Подсказки» читает общий read-only журнал всех подсказок всех
	// субъектов; фильтры статус/группа/характер и ограничение размера ответа
	// применяются сервером в одном контракте.
	// Traceability: doc:.wf-research/ui-concept/concept.md#6-раздел-подсказки-маршрут-hints
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfHintsLogServesFilteredPagedRecords()
	{
		// Arrange: стабильная read-модель с журналом портфельной и конструкционной записи.
		await using var factory = new SectionApiFactory(services => services.ReplaceHintDisplay(new StubHintDisplayReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос журнала с фильтром группы и лимитом страницы.
		var response = await client.GetAsync("/api/v1/hints/log?group=futures-leg&limit=1&offset=0");

		// Assert: ответ несёт страницу записей с субъектом, группой и источниками.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["total"]!.GetValue<int>(), Is.EqualTo(1));
		Assert.That(payload["limit"]!.GetValue<int>(), Is.EqualTo(1));
		var item = payload["items"]!.AsArray().Single()!;
		Assert.That(item["subject"]!["kind"]!.GetValue<string>(), Is.EqualTo("construction"));
		Assert.That(item["subject"]!["constructionId"]!.GetValue<long>(), Is.EqualTo(7L));
		Assert.That(item["group"]!["id"]!.GetValue<string>(), Is.EqualTo("futures-leg"));
		Assert.That(item["character"]!.GetValue<string>(), Is.EqualTo("futures-leg"));
	}

	[TestMethod]
	[Description("Журнал подсказок отвечает 400 на некорректный статус фильтра")]
	// Некорректный статус фильтра — ошибка контракта запроса: endpoint не
	// подставляет дефолт и сообщает причину 400.
	// Traceability: doc:.wf-research/ui-concept/concept.md#6-раздел-подсказки-маршрут-hints
	public async Task ThrowOnHintsLogWithInvalidStatusReturns400()
	{
		// Arrange: стабильная read-модель подсказок.
		await using var factory = new SectionApiFactory(services => services.ReplaceHintDisplay(new StubHintDisplayReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос с невалидным статусом фильтра.
		var response = await client.GetAsync("/api/v1/hints/log?status=invalid");

		// Assert: endpoint отвечает ошибкой контракта 400.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["error"]!.GetValue<string>(), Does.Contain("Недопустимый статус"));
	}

	[TestMethod]
	[Description("Кнопка синхронизации запускает запуск и возвращает его итог")]
	// Компактная кнопка «Синхронизировать» шапки: команда идёт через единый API,
	// итог закрытого запуска (режим, статус, счётчики новых записей) возвращается
	// ответом; подробности остаются разделу «Синхронизация».
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfSyncRunReturnsRunSummary()
	{
		// Arrange: стабильный сервис синхронизации с успешным инкрементальным запуском.
		await using var factory = new SectionApiFactory(services => services.ReplaceSyncService(new StubJournalSyncService()));
		using var client = factory.CreateClient();

		// Act: команда синхронизации из шапки раздела.
		var response = await client.PostAsync("/api/v1/sync", content: null);

		// Assert: итог запуска приходит ответом команды.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["mode"]!.GetValue<string>(), Is.EqualTo("incremental"));
		Assert.That(payload["status"]!.GetValue<string>(), Is.EqualTo("succeeded"));
		Assert.That(payload["startedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-20T14:30:00+00:00"));
		Assert.That(payload["finishedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-20T14:31:30+00:00"));
		Assert.That(payload["newExecutions"]!.GetValue<int>(), Is.EqualTo(5));
		Assert.That(payload["newDeliveries"]!.GetValue<int>(), Is.EqualTo(2));
		Assert.That(payload["newInstruments"]!.GetValue<int>(), Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Прерванная биржей синхронизация отвечает 502 с текстом ошибки")]
	// Ошибка биржи закрывает запуск со статусом Failed: шапка показывает
	// состояние сбоя с причиной, а не молча проглатывает ошибку.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	public async Task ThrowOnBybitFailureReturns502WithReason()
	{
		// Arrange: сервис синхронизации, падающий ошибкой биржи.
		await using var factory = new SectionApiFactory(services => services.ReplaceSyncService(StubJournalSyncService.Failing()));
		using var client = factory.CreateClient();

		// Act: команда синхронизации из шапки раздела.
		var response = await client.PostAsync("/api/v1/sync", content: null);

		// Assert: команда отвечает 502 с причиной сбоя.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["error"]!.GetValue<string>(), Does.Contain("Bybit"));
	}

	[TestMethod]
	[Description("OpenAPI-документ содержит эндпоинты раздела «Конструкции»")]
	// Контракт раздела публикуется в OpenAPI: документ отражает фактические
	// эндпоинты, пригоден для ревью контрактов и генерации клиента SPA.
	// Traceability: openspec:http-api/transport#scenario-openapi-schema-published
	public async Task TryIfOpenApiListsConstructionsSectionEndpoints()
	{
		// Arrange: хост раздела со стабильными read-моделями.
		await using var factory = new SectionApiFactory(services => services.ReplaceReadModel(new StubListReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос OpenAPI-документа.
		var document = await (await client.GetAsync("/openapi/v1.json")).Content.ReadFromJsonAsync<JsonNode>();

		// Assert: пути раздела опубликованы под версионированным префиксом.
		var paths = document!["paths"]!.AsObject().Select(path => path.Key).ToArray();
		Assert.That(paths, Does.Contain("/api/v1/constructions"));
		Assert.That(paths, Does.Contain("/api/v1/hints/log"));
	}
}

/// <summary>Помощники подмены доменных зависимостей раздела стабильными заглушками.</summary>
internal static class SectionApiServiceExtensions
{
	/// <summary>Заменяет регистрацию read-модели списка конструкций стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceReadModel(this IServiceCollection services, IConstructionListReadModel readModel)
	{
		foreach (var existing in services.Where(service => service.ServiceType == typeof(IConstructionListReadModel)).ToArray())
		{
			services.Remove(existing);
		}

		services.Add(ServiceDescriptor.Singleton(readModel));
		return services;
	}

	/// <summary>Заменяет регистрацию read-модели отображения подсказок стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceHintDisplay(this IServiceCollection services, IHintDisplayReadModel readModel)
	{
		foreach (var existing in services.Where(service => service.ServiceType == typeof(IHintDisplayReadModel)).ToArray())
		{
			services.Remove(existing);
		}

		services.Add(ServiceDescriptor.Singleton(readModel));
		return services;
	}

	/// <summary>Заменяет регистрацию read-модели деталей конструкции стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceDetailReadModel(this IServiceCollection services, IConstructionDetailReadModel readModel)
	{
		foreach (var existing in services.Where(service => service.ServiceType == typeof(IConstructionDetailReadModel)).ToArray())
		{
			services.Remove(existing);
		}

		services.Add(ServiceDescriptor.Singleton(readModel));
		return services;
	}

	/// <summary>Заменяет регистрацию раннера прохода подсказок стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceHintPassRunner(this IServiceCollection services, IHintPassRunner runner)
	{
		foreach (var existing in services.Where(service => service.ServiceType == typeof(IHintPassRunner)).ToArray())
		{
			services.Remove(existing);
		}

		services.Add(ServiceDescriptor.Singleton(runner));
		return services;
	}

	/// <summary>Заменяет регистрацию сервиса синхронизации стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceSyncService(this IServiceCollection services, IJournalSyncService syncService)
	{
		foreach (var existing in services.Where(service => service.ServiceType == typeof(IJournalSyncService)).ToArray())
		{
			services.Remove(existing);
		}

		services.Add(ServiceDescriptor.Singleton(syncService));
		return services;
	}
}

/// <summary>
/// Хост раздела «Конструкции» для интеграционных проверок API: реальная точка
/// входа Program на временной базе SQLite, доменные read-модели раздела
/// подменяются заглушками через ConfigureTestServices — проверяется
/// HTTP-контракт, а не доменные вычисления. Тест может доопределить значения
/// конфигурации: они перекрывают appsettings и переменные окружения.
/// </summary>
internal sealed class SectionApiFactory : WebApplicationFactory<Program>
{
	private readonly string _databasePath = Path.Combine(
		Path.GetDirectoryName(typeof(ConstructionsSectionApiTests).Assembly.Location)!,
		$"journal-section-api-tests-{Guid.NewGuid():N}.db");

	private readonly Action<IServiceCollection>? _configureServices;

	private readonly Action<IDictionary<string, string?>>? _configureConfiguration;

	public SectionApiFactory(
		Action<IServiceCollection>? configureServices = null,
		Action<IDictionary<string, string?>>? configureConfiguration = null)
	{
		_configureServices = configureServices;
		_configureConfiguration = configureConfiguration;
	}

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Development: без HSTS и производственного обработчика ошибок.
		builder.UseEnvironment("Development");
		// Временная база каталога прогона вместо App_Data журнала; тестовые
		// значения добавляются тем же источником и перекрывают окружение.
		builder.ConfigureAppConfiguration((_, config) =>
		{
			var settings = new Dictionary<string, string?>
			{
				["ConnectionStrings:Journal"] = $"Data Source={_databasePath}",
			};
			_configureConfiguration?.Invoke(settings);
			config.AddInMemoryCollection(settings);
		});
		// Стабильные заглушки доменных зависимостей поверх реальных регистраций.
		builder.ConfigureTestServices(services => _configureServices?.Invoke(services));
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
		if (disposing == false)
		{
			return;
		}

		// Временная база и соседние WAL-файлы удаляются после прогона.
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			try
			{
				File.Delete(_databasePath + suffix);
			}
			catch (IOException)
			{
			}
		}
	}
}

/// <summary>Стабильная read-модель списка: одна открытая и одна закрытая конструкции.</summary>
internal sealed class StubListReadModel : IConstructionListReadModel
{
	public const long OpenConstructionId = 7L;

	public const long ClosedConstructionId = 8L;

	private readonly ConstructionListData _data;

	public StubListReadModel()
		: this(CreateDefaultData())
	{
	}

	private StubListReadModel(ConstructionListData data)
	{
		_data = data;
	}

	private static ConstructionListData CreateDefaultData()
	{
		var marksAsOf = new DateTimeOffset(2026, 6, 20, 14, 30, 0, TimeSpan.Zero);
		return new ConstructionListData(
			TotalPnL: 100.5m,
			RealizedPnL: 60.25m,
			UnrealizedPnL: 40.25m,
			MarksAsOf: marksAsOf,
			HasMarkFailure: false,
			ConstructionCount: 2,
			OpenCount: 1,
			Items:
			[
				new ConstructionListItem(
					ConstructionId: OpenConstructionId,
					Name: "ETH-240628-3200C+P",
					Status: ConstructionStatus.Open,
					AllocatedCapitalUsdt: 3000m,
					RiskPercent: 3m,
					RiskUsdt: 90m,
					ProfitPercent: 8m,
					ProfitUsdt: 240m,
					RealizedPnL: 60.25m,
					UnrealizedPnL: 40.25m,
					AdjustmentsPnL: 0m,
					TotalPnL: 100.5m,
					TotalPnLPercent: 3.35m,
					OpenedAt: new DateTimeOffset(2026, 6, 18, 9, 5, 0, TimeSpan.Zero),
					ClosedAt: null,
					MarkValue: 500m,
					CapitalUsagePercent: 16.7m,
					RealRiskUsdt: 150m),
				new ConstructionListItem(
					ConstructionId: ClosedConstructionId,
					Name: "BTC-240531-60000C",
					Status: ConstructionStatus.Closed,
					AllocatedCapitalUsdt: null,
					RiskPercent: null,
					RiskUsdt: null,
					ProfitPercent: null,
					ProfitUsdt: null,
					RealizedPnL: 0m,
					UnrealizedPnL: 0m,
					AdjustmentsPnL: 0m,
					TotalPnL: 0m,
					TotalPnLPercent: null,
					OpenedAt: new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
					ClosedAt: new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero),
					MarkValue: null,
					CapitalUsagePercent: null,
					RealRiskUsdt: null),
			]);
	}

	/// <summary>Вариант со сбоем марок: нереализованные величины и итоги null.</summary>
	public static StubListReadModel WithMarkFailure() => new(new ConstructionListData(
			TotalPnL: null,
			RealizedPnL: 60.25m,
			UnrealizedPnL: null,
			MarksAsOf: null,
			HasMarkFailure: true,
			ConstructionCount: 1,
			OpenCount: 1,
			Items:
			[
				new ConstructionListItem(
					ConstructionId: OpenConstructionId,
					Name: "ETH-240628-3200C+P",
					Status: ConstructionStatus.Open,
					AllocatedCapitalUsdt: 3000m,
					RiskPercent: 3m,
					RiskUsdt: 90m,
					ProfitPercent: 8m,
					ProfitUsdt: 240m,
					RealizedPnL: 60.25m,
					UnrealizedPnL: null,
					AdjustmentsPnL: 0m,
					TotalPnL: null,
					TotalPnLPercent: null,
					OpenedAt: new DateTimeOffset(2026, 6, 18, 9, 5, 0, TimeSpan.Zero),
					ClosedAt: null,
					MarkValue: null,
					CapitalUsagePercent: null,
					RealRiskUsdt: 150m),
			]));

	public Task<ConstructionListData> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_data);
}

/// <summary>Стабильная read-модель деталей: конструкция 7 с метриками и таблицами записей.</summary>
internal sealed class StubDetailReadModel : IConstructionDetailReadModel
{
	public Task<ConstructionDetailData> ReadAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		if (constructionId != StubListReadModel.OpenConstructionId)
		{
			throw new ConstructionNotFoundException(constructionId);
		}

		var marksAsOf = new DateTimeOffset(2026, 6, 20, 14, 30, 0, TimeSpan.Zero);
		var openedAt = new DateTimeOffset(2026, 6, 18, 9, 5, 0, TimeSpan.Zero);
		var metrics = new ConstructionMetrics
		{
			ConstructionId = constructionId,
			AllocatedCapitalUsdt = 3000m,
			RealizedPnL = 60.25m,
			UnrealizedPnL = 40.25m,
			AdjustmentsPnL = 5m,
			TotalPnL = 105.5m,
			MarkValue = 500m,
			RealizedPnLPercent = 2.0m,
			UnrealizedPnLPercent = 1.34m,
			AdjustmentsPnLPercent = 0.17m,
			TotalPnLPercent = 3.5m,
			CapitalUsagePercent = 16.7m,
			RealRiskUsdt = 150m,
			OpenedAt = openedAt,
			ClosedAt = null,
			Duration = TimeSpan.FromHours(53.4),
		};
		var data = new ConstructionDetailData(
			ConstructionId: constructionId,
			Name: "ETH-240628-3200C+P",
			Status: ConstructionStatus.Open,
			AllocatedCapitalUsdt: 3000m,
			RiskPercent: 3m,
			RiskUsdt: 90m,
			ProfitPercent: 8m,
			ProfitUsdt: 240m,
			Comment: null,
			Metrics: metrics,
			HasOpenResidual: true,
			HasMarkFailure: false,
			MarksAsOf: marksAsOf,
			Positions:
			[
				new ConstructionPositionRow(
					Symbol: "ETH-28JUN24-3200-C",
					Residual: 2m,
					AverageEntryPrice: 30m,
					AverageClosePrice: null,
					RealizedPnL: 40m,
					RealizedPnLPercent: 1.33m,
					UnrealizedPnL: 30m,
					UnrealizedPnLPercent: 1m,
					TotalPnL: 70m,
					TotalPnLPercent: 2.33m,
					AccumulatedFees: 0.5m,
					OpenedAt: openedAt,
					ClosedAt: null,
					IsOpen: true,
					Comment: null,
					MarkValue: 300m,
					PriceChangePercent: 5m),
				new ConstructionPositionRow(
					Symbol: "ETHUSDT",
					Residual: 0m,
					AverageEntryPrice: 3500m,
					AverageClosePrice: 3520m,
					RealizedPnL: 20.25m,
					RealizedPnLPercent: 0.67m,
					UnrealizedPnL: 10.25m,
					UnrealizedPnLPercent: 0.34m,
					TotalPnL: 30.5m,
					TotalPnLPercent: 1.02m,
					AccumulatedFees: 0.25m,
					OpenedAt: openedAt,
					ClosedAt: new DateTimeOffset(2026, 6, 19, 10, 0, 0, TimeSpan.Zero),
					IsOpen: false,
					Comment: null,
					MarkValue: 200m,
					PriceChangePercent: null),
			],
			Trades:
			[
				new ConstructionTradeRow("exec-1", "ETH-28JUN24-3200-C", openedAt, true, 2m, 30m, 60m, 0.1m, null),
				new ConstructionTradeRow("exec-2", "ETHUSDT", openedAt.AddHours(1), true, 0.5m, 3500m, 1750m, 0.2m, null),
				new ConstructionTradeRow("exec-3", "ETHUSDT", openedAt.AddHours(2), false, 0.5m, 3520m, 1760m, 0.15m, null),
			],
			ClosingEntries: [],
			ClosingWarnings: [],
			Adjustments:
			[
				new ConstructionAdjustmentRow(11, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero), "Пополнение комиссии", PnLAdjustmentSource.Manual, 5m),
			]);
		return Task.FromResult(data);
	}
}

/// <summary>Стабильная read-модель подсказок: панели, счётчики и команды переходов.</summary>
internal sealed class StubHintDisplayReadModel : IHintDisplayReadModel
{
	/// <summary>Идентификаторы записей, переведённых командой «Применено».</summary>
	public IReadOnlyList<long> AppliedHintIds => _appliedHintIds;

	/// <summary>Идентификаторы записей с автопометкой первого показа.</summary>
	public IReadOnlyList<long> SeenHintIds => _seenHintIds;

	private readonly List<long> _appliedHintIds = [];

	private readonly List<long> _seenHintIds = [];

	private readonly HintRecord _journalLiveHint = new()
	{
		Id = 5,
		RuleId = "risk-limit-week",
		Subject = HintSubject.ForJournal(),
		Character = "risk-mode",
		Clarity = "crisp",
		Sources = [new HintSourceTag { Tag = "ПИ", File = "risk/limits.md", Quotes = ["Лимит риска недели"] }],
		Text = "Лимит риска недели достигнут: 2.1% использовано",
		Facts = new Dictionary<string, string> { ["weekPnl"] = "2.1%" },
		AsOf = new DateTimeOffset(2026, 6, 20, 14, 30, 0, TimeSpan.Zero),
		Status = HintStatus.New,
	};

	public Task<HintPanelData> ReadJournalPanelAsync(CancellationToken cancellationToken = default)
	{
		var panel = new HintPanelData
		{
			Subject = HintSubject.ForJournal(),
			LiveGroups = [new HintSection { Group = HintSectionGroups.V1[0], Hints = [_journalLiveHint] }],
			History = [AppliedRecord()],
		};
		return Task.FromResult(panel);
	}

	public Task<HintPanelData> ReadConstructionPanelAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		var panel = new HintPanelData
		{
			Subject = HintSubject.ForConstruction(constructionId),
			LiveGroups = [new HintSection { Group = HintSectionGroups.V1[0], Hints = [_journalLiveHint] }],
			History = [],
		};
		return Task.FromResult(panel);
	}

	public Task<IReadOnlyDictionary<long, int>> ReadLiveCountsByConstructionAsync(CancellationToken cancellationToken = default)
	{
		var counts = new Dictionary<long, int> { [StubListReadModel.OpenConstructionId] = 2 };
		return Task.FromResult<IReadOnlyDictionary<long, int>>(counts);
	}

	public Task<IReadOnlyList<HintRecord>> ReadLogAsync(HintLogFilter? filter = null, CancellationToken cancellationToken = default)
	{
		var log = new List<HintRecord>
		{
			_journalLiveHint,
			AppliedRecord(),
			new()
			{
				Id = 13,
				RuleId = "futures-bias",
				Subject = HintSubject.ForConstruction(StubListReadModel.OpenConstructionId),
				Character = "futures-leg",
				Clarity = "crisp",
				Sources = [new HintSourceTag { Tag = "ЛИЧ", File = "futures/discipline.md", Quotes = ["Держи нагрузку фьючерса в рамках"] }],
				Text = "Снизить нагрузку фьючерсной ноги",
				Facts = new Dictionary<string, string> { ["leverage"] = "x6" },
				AsOf = new DateTimeOffset(2026, 6, 20, 16, 0, 0, TimeSpan.Zero),
				Status = HintStatus.New,
			},
		};

		var groupCharacters = filter?.GroupCharacters;
		var filtered = log
			.Where(record => filter?.Status is null || record.Status == filter.Status)
			.Where(record => filter?.Character is null || record.Character == filter.Character)
			.Where(record => groupCharacters is null || groupCharacters.Contains(record.Character, StringComparer.Ordinal))
			.OrderByDescending(record => record.AsOf)
			.ThenByDescending(record => record.Id)
			.ToArray();

		return Task.FromResult<IReadOnlyList<HintRecord>>(filtered);
	}

	public Task<bool> ApplyAsync(long hintId, CancellationToken cancellationToken = default)
	{
		if (_appliedHintIds.Contains(hintId))
		{
			// Терминальная запись: повторный переход не выполняется.
			return Task.FromResult(false);
		}

		_appliedHintIds.Add(hintId);
		return Task.FromResult(true);
	}

	public Task<bool> DismissAsync(long hintId, CancellationToken cancellationToken = default) => Task.FromResult(false);

	public Task MarkSeenAsync(long hintId, DateTimeOffset seenAt, CancellationToken cancellationToken = default)
	{
		_seenHintIds.Add(hintId);
		return Task.CompletedTask;
	}

	private static HintRecord AppliedRecord() => new()
	{
		Id = 9,
		RuleId = "roll-window",
		Subject = HintSubject.ForJournal(),
		Character = "rolling",
		Clarity = "fuzzy",
		Sources = [],
		Text = "Окно ролла закрыто",
		Facts = new Dictionary<string, string>(),
		AsOf = new DateTimeOffset(2026, 6, 19, 10, 0, 0, TimeSpan.Zero),
		Status = HintStatus.Applied,
		FirstSeenAt = new DateTimeOffset(2026, 6, 19, 10, 5, 0, TimeSpan.Zero),
	};
}

/// <summary>Стабильный раннер прохода: возвращает заданный итог прохода.</summary>
internal sealed class StubHintPassRunner(HintPassResult result) : IHintPassRunner
{
	public static HintPassResult Completed() => new()
	{
		Outcome = HintPassOutcome.Completed,
		AsOf = new DateTimeOffset(2026, 6, 20, 14, 30, 0, TimeSpan.Zero),
		CreatedHints = 2,
		ExpiredHints = 1,
	};

	public static HintPassResult CorpusInvalid() => new()
	{
		Outcome = HintPassOutcome.CorpusInvalid,
		AsOf = new DateTimeOffset(2026, 6, 20, 14, 30, 0, TimeSpan.Zero),
		Diagnostics = ["Карточка risk-limit-week: пустой текст подсказки"],
	};

	public Task<HintPassResult> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
}

/// <summary>Стабильный сервис синхронизации: один успешный или один падающий запуск.</summary>
internal sealed class StubJournalSyncService : IJournalSyncService
{
	private readonly Exception? _failure;

	public StubJournalSyncService(Exception? failure = null)
	{
		_failure = failure;
	}

	/// <summary>Вариант, падающий ошибкой биржи.</summary>
	public static StubJournalSyncService Failing() => new(new BybitApiException(10001, "сбой шлюза"));

	public Task<JournalSyncResult> SyncAsync(CancellationToken cancellationToken = default)
	{
		if (_failure is not null)
		{
			throw _failure;
		}

		var result = new JournalSyncResult
		{
			Mode = SyncRunMode.Incremental,
			Run = new SyncRun
			{
				Id = 42,
				StartedAt = new DateTimeOffset(2026, 6, 20, 14, 30, 0, TimeSpan.Zero),
				FinishedAt = new DateTimeOffset(2026, 6, 20, 14, 31, 30, TimeSpan.Zero),
				Mode = SyncRunMode.Incremental,
				Status = SyncRunStatus.Succeeded,
				NewExecutions = 5,
				NewDeliveries = 2,
				NewInstruments = 1,
			},
			Executions = new Dictionary<string, ExecutionCategorySyncResult>(),
			Deliveries = new Dictionary<string, DeliveryCategorySyncResult>(),
		};
		return Task.FromResult(result);
	}
}
