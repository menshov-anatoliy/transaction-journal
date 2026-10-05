namespace TransactionJournal.Tests.Infrastructure.Consultations;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Infrastructure.Consultations;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Адаптер хранилища консультаций на SQLite per construction: диалог создаётся
/// первым сообщением, удаляется целиком без восстановления, истории диалогов
/// и конструкций изолированы, рыночный след сохраняется в сообщении.
/// </summary>
[TestClass]
public class ConsultationStoreTests
{
	private static readonly DateTimeOffset AsOf = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

	private string _directory = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой папкой per-construction баз.
		_directory = Path.Combine(Path.GetTempPath(), $"consultation-store-tests-{Guid.NewGuid():N}");
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файлы баз открытыми — сбрасываем перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	/// <summary>Хранилище над папкой проверки.</summary>
	private ConsultationStore CreateStore() => new(_directory);

	/// <summary>Черновик сообщения владельца с заданным текстом.</summary>
	private static ConsultationMessageDraft UserDraft(string text) => new()
	{
		Role = ConsultationMessageRole.User,
		Text = text,
		AsOf = AsOf,
	};

	/// <summary>Черновик ответа ассистента без рыночного следа.</summary>
	private static ConsultationMessageDraft AssistantDraft(string text) => new()
	{
		Role = ConsultationMessageRole.Assistant,
		Text = text,
		AsOf = AsOf,
	};

	[TestMethod]
	[Description("Первое сообщение с dialogueId = null создаёт новый диалог конструкции")]
	// Отдельной команды создания диалога нет: диалог появляется первым
	// сообщением владельца, момент создания — as-of этого сообщения.
	// Traceability: openspec:consultations/history#scenario-history-created-by-first-message
	public async Task TryIfFirstMessage_CreatesNewDialogue()
	{
		// Arrange
		var store = CreateStore();

		// Act
		var saved = await store.AppendMessageAsync(7, null, UserDraft("Вопрос по конструкции."));

		// Assert
		var dialogues = await store.ListDialoguesAsync(7);
		Assert.That(dialogues, Has.Count.EqualTo(1));
		Assert.That(dialogues[0].Id, Is.EqualTo(saved.DialogueId));
		Assert.That(dialogues[0].ConstructionId, Is.EqualTo(7));
		Assert.That(dialogues[0].CreatedAt, Is.EqualTo(AsOf));
		var messages = await store.ListMessagesAsync(7, saved.DialogueId);
		Assert.That(messages, Has.Count.EqualTo(1));
		Assert.That(messages[0].Role, Is.EqualTo(ConsultationMessageRole.User));
		Assert.That(messages[0].Text, Is.EqualTo("Вопрос по конструкции."));
	}

	[TestMethod]
	[Description("Последующие сообщения с идентификатором диалога попадают в тот же диалог")]
	// Диалог создаётся только первым сообщением: продолжение обмена пишет
	// в существующий диалог, новых диалогов не появляется.
	// Traceability: openspec:consultations/history#scenario-history-created-by-first-message
	public async Task TryIfFollowUpMessage_AppendsToExistingDialogue()
	{
		// Arrange
		var store = CreateStore();
		var first = await store.AppendMessageAsync(7, null, UserDraft("Первый вопрос."));

		// Act
		var reply = await store.AppendMessageAsync(7, first.DialogueId, AssistantDraft("Ответ."));
		var followUp = await store.AppendMessageAsync(7, first.DialogueId, UserDraft("Уточнение."));

		// Assert
		Assert.That(reply.DialogueId, Is.EqualTo(first.DialogueId));
		Assert.That(followUp.DialogueId, Is.EqualTo(first.DialogueId));
		Assert.That(await store.ListDialoguesAsync(7), Has.Count.EqualTo(1));
		Assert.That(await store.ListMessagesAsync(7, first.DialogueId), Has.Count.EqualTo(3));
	}

	[TestMethod]
	[Description("Удаление диалога вычищает его сообщения целиком, чужие диалоги и конструкции не задеты")]
	// Hard delete: диалог удаляется со всеми сообщениями без корзины и
	// восстановления; повторное удаление и чужой идентификатор возвращают false.
	// Traceability: openspec:consultations/history#scenario-history-hard-delete-dialogue
	public async Task TryIfDeleteDialogue_RemovesDialogueWithAllMessages()
	{
		// Arrange
		var store = CreateStore();
		var removed = await store.AppendMessageAsync(7, null, UserDraft("Первый вопрос."));
		var staying = await store.AppendMessageAsync(7, null, UserDraft("Второй вопрос."));
		await store.AppendMessageAsync(7, removed.DialogueId, AssistantDraft("Ответ ассистента."));

		// Act
		var deleted = await store.DeleteDialogueAsync(7, removed.DialogueId);
		var deletedAgain = await store.DeleteDialogueAsync(7, removed.DialogueId);
		var unknown = await store.DeleteDialogueAsync(7, 999);
		var foreign = await store.DeleteDialogueAsync(8, removed.DialogueId);

		// Assert
		Assert.That(deleted, Is.True);
		Assert.That(deletedAgain, Is.False);
		Assert.That(unknown, Is.False);
		Assert.That(foreign, Is.False);
		var dialogues = await store.ListDialoguesAsync(7);
		Assert.That(dialogues.Select(dialogue => dialogue.Id), Is.EquivalentTo(new[] { staying.DialogueId }));
		Assert.That(await store.ListMessagesAsync(7, removed.DialogueId), Is.Empty);
		Assert.That(await store.ListMessagesAsync(7, staying.DialogueId), Has.Count.EqualTo(1));
	}

	[TestMethod]
	[Description("История одного диалога не видна из соседнего диалога той же конструкции")]
	// Изоляция: выборка сообщений фильтруется по диалогу — ассистенту видна
	// история только его диалога, соседние диалоги в неё не попадают.
	// Traceability: openspec:consultations/history#scenario-history-dialogues-isolated
	public async Task TryIfDialogues_ListMessagesOnlyOwnHistory()
	{
		// Arrange
		var store = CreateStore();
		var first = await store.AppendMessageAsync(7, null, UserDraft("Вопрос первого диалога."));
		var second = await store.AppendMessageAsync(7, null, UserDraft("Вопрос второго диалога."));
		await store.AppendMessageAsync(7, second.DialogueId, AssistantDraft("Ответ второго диалога."));

		// Act
		var firstMessages = await store.ListMessagesAsync(7, first.DialogueId);
		var secondMessages = await store.ListMessagesAsync(7, second.DialogueId);

		// Assert
		Assert.That(firstMessages, Has.Count.EqualTo(1));
		Assert.That(firstMessages[0].Text, Is.EqualTo("Вопрос первого диалога."));
		Assert.That(
			secondMessages.Select(message => message.Text),
			Is.EquivalentTo(new[] { "Вопрос второго диалога.", "Ответ второго диалога." }));
	}

	[TestMethod]
	[Description("Истории разных конструкций изолированы: базы per construction не пересекаются")]
	// Запись окружения per construction: у каждой конструкции своя база —
	// диалоги и сообщения одной конструкции не видны из другой.
	// Traceability: openspec:consultations/history#requirement-history-environment-record
	public async Task TryIfConstructions_ListDialoguesOnlyOwn()
	{
		// Arrange
		var store = CreateStore();
		var mine = await store.AppendMessageAsync(7, null, UserDraft("Вопрос конструкции 7."));
		await store.AppendMessageAsync(8, null, UserDraft("Вопрос конструкции 8."));

		// Act
		var dialogues = await store.ListDialoguesAsync(7);
		var messages = await store.ListMessagesAsync(7, mine.DialogueId);

		// Assert
		Assert.That(dialogues.Select(dialogue => dialogue.Id), Is.EquivalentTo(new[] { mine.DialogueId }));
		Assert.That(messages, Has.Count.EqualTo(1));
		Assert.That(messages[0].Text, Is.EqualTo("Вопрос конструкции 7."));
	}

	[TestMethod]
	[Description("Рыночный след ответа ассистента сохраняется в сообщении и восстанавливается при чтении")]
	// След хранит вызванные инструменты и as-of их данных, включая отсутствие
	// данных; у сообщений владельца следа нет.
	// Traceability: openspec:consultations/history#requirement-history-message-composition
	public async Task TryIfAssistantMessage_KeepsMarketTrace()
	{
		// Arrange
		var store = CreateStore();
		var dialogue = await store.AppendMessageAsync(7, null, UserDraft("Что с рынком?"));
		var trace = new ConsultationMarketTrace
		{
			Invocations =
			[
				new ConsultationToolInvocation
				{
					ToolName = "get_market_snapshot",
					Arguments = "{\"baseCoin\":\"BTC\"}",
					DataAsOf = AsOf,
				},
				new ConsultationToolInvocation
				{
					ToolName = "get_option_board",
					Arguments = "{\"baseCoin\":\"BTC\"}",
					DataAsOf = null,
				},
			],
		};

		// Act
		await store.AppendMessageAsync(7, dialogue.DialogueId, new ConsultationMessageDraft
		{
			Role = ConsultationMessageRole.Assistant,
			Text = "По журналу и доске опционов…",
			AsOf = AsOf,
			MarketTrace = trace,
		});
		var messages = await store.ListMessagesAsync(7, dialogue.DialogueId);

		// Assert
		var stored = messages.Single(message => message.Role == ConsultationMessageRole.Assistant);
		Assert.That(stored.MarketTrace, Is.Not.Null);
		var invocations = stored.MarketTrace!.Invocations;
		Assert.That(invocations, Has.Count.EqualTo(2));
		Assert.That(invocations[0].ToolName, Is.EqualTo("get_market_snapshot"));
		Assert.That(invocations[0].DataAsOf, Is.EqualTo(AsOf));
		Assert.That(invocations[1].ToolName, Is.EqualTo("get_option_board"));
		Assert.That(invocations[1].DataAsOf, Is.Null);
		var userMessage = messages.Single(message => message.Role == ConsultationMessageRole.User);
		Assert.That(userMessage.MarketTrace, Is.Null);
	}

	[TestMethod]
	[Description("Стирание консультаций конструкции убирает все её диалоги и сообщения, чужие конструкции не задеты")]
	// Запись окружения одноразова: стирание консультаций конструкции
	// вычищает её базу целиком — история живёт и умирает с конструкцией.
	// Traceability: openspec:consultations/history#scenario-history-rebuild-wipes
	public async Task TryIfDeleteForConstruction_WipesAllDialoguesAndMessages()
	{
		// Arrange
		var store = CreateStore();
		var dialogue = await store.AppendMessageAsync(7, null, UserDraft("Вопрос."));
		await store.AppendMessageAsync(7, dialogue.DialogueId, AssistantDraft("Ответ."));
		await store.AppendMessageAsync(8, null, UserDraft("Чужой вопрос."));

		// Act
		await store.DeleteForConstructionAsync(7);

		// Assert
		Assert.That(await store.ListDialoguesAsync(7), Is.Empty);
		Assert.That(await store.ListDialoguesAsync(8), Has.Count.EqualTo(1));
	}

	[TestMethod]
	[Description("Сообщение в несуществующий диалог чужой конструкции отвергается исключением")]
	// Диалог создаётся только первым сообщением: обращение к чужому или
	// несуществующему диалогу — ошибка вызывающего, хранилище её не маскирует.
	// Traceability: openspec:consultations/history#scenario-history-created-by-first-message
	[ExpectedException(typeof(InvalidOperationException))]
	public async Task ThrowOnAppendToUnknownDialogue()
	{
		// Arrange
		var store = CreateStore();

		// Act
		await store.AppendMessageAsync(7, 999, UserDraft("Вопрос."));

		// Assert
		Assert.Fail("Ожидалось исключение для несуществующего диалога.");
	}

	[TestMethod]
	[Description("Черновик сообщения без значения отвергается исключением")]
	// Пустой черновик не несёт роли, текста и as-of — сохранять нечего.
	// Traceability: openspec:consultations/history#requirement-history-message-composition
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnAppendNullDraft()
	{
		// Arrange
		var store = CreateStore();

		// Act
		await store.AppendMessageAsync(7, null, null!);

		// Assert
		Assert.Fail("Ожидалось исключение для пустого черновика.");
	}

	[TestMethod]
	[Description("Хранилище без папки баз не создаётся")]
	// Путь папки обязателен: без него файлы per-construction баз некуда класть.
	// Traceability: openspec:consultations/history#requirement-history-environment-record
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnCreateStoreWithoutDirectory()
	{
		// Act
		_ = new ConsultationStore("   ");

		// Assert
		Assert.Fail("Ожидалось исключение для пустого пути папки.");
	}

	[TestMethod]
	[Description("Неположительный идентификатор конструкции отвергается исключением")]
	// Идентификатор конструкции попадает в имя файла базы — ноль и
	// отрицательные значения не должны порождать файлы.
	// Traceability: openspec:consultations/history#requirement-history-environment-record
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public async Task ThrowOnNonPositiveConstructionId()
	{
		// Arrange
		var store = CreateStore();

		// Act
		await store.ListDialoguesAsync(0);

		// Assert
		Assert.Fail("Ожидалось исключение для неположительного идентификатора конструкции.");
	}
}
