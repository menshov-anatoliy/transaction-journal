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
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application.Ops;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки API карточки конструкции: полный снимок экрана
/// (шапка с параметрами и комментарием, метрики с периодом, четыре таблицы
/// записей) и команды действий — шапка, комментарии трёх уровней, ручные
/// пометки закрытия, действия сделок и корректировки PnL. Домен подменяется
/// стабильными заглушками: проверяется HTTP-контракт карточки, а не повторное
/// вычисление метрик.
/// </summary>
[TestClass]
public sealed class ConstructionCardApiTests
{
	[TestMethod]
	[Description("Снимок карточки отдаёт шапку с параметрами, метрики с периодом и четыре таблицы")]
	// Карточка конструкции — полная информация и всё управление (паритет №3–№9):
	// один запрос снимка отдаёт шапку (имя, статус, капитал, риск/профит с
	// единицами, комментарий), kstrip метрик с периодом и длительностью
	// и таблицы позиций, сделок, закрывающих записей и корректировок.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	// Все данные карточки идут через единый версионированный API.
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfCardSnapshotServesHeaderMetricsAndTables()
	{
		// Arrange: стабильная read-модель деталей конструкции 7.
		await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(new StubCardDetailReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос снимка карточки.
		var response = await client.GetAsync("/api/v1/constructions/7");

		// Assert: успешный JSON полного снимка.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();

		// Шапка: идентификация, параметры плановых границ обеими единицами
		// с первоисточником-единицей и комментарий.
		Assert.That(payload!["constructionId"]!.GetValue<long>(), Is.EqualTo(7L));
		Assert.That(payload["name"]!.GetValue<string>(), Is.EqualTo("ETH-240628-3200C+P"));
		Assert.That(payload["status"]!.GetValue<string>(), Is.EqualTo("open"));
		Assert.That(payload["allocatedCapitalUsdt"]!.GetValue<decimal>(), Is.EqualTo(3000m));
		Assert.That(payload["riskPercent"]!.GetValue<decimal>(), Is.EqualTo(3m));
		Assert.That(payload["riskUsdt"]!.GetValue<decimal>(), Is.EqualTo(90m));
		Assert.That(payload["riskUnit"]!.GetValue<string>(), Is.EqualTo("percent"));
		Assert.That(payload["profitPercent"]!.GetValue<decimal>(), Is.EqualTo(8m));
		Assert.That(payload["profitUsdt"]!.GetValue<decimal>(), Is.EqualTo(240m));
		Assert.That(payload["profitUnit"]!.GetValue<string>(), Is.EqualTo("usdt"));
		Assert.That(payload["comment"]!.GetValue<string>(), Is.EqualTo("## стреддл\n*имя ручное*"));

		// Метрики: итог с процентом, разбивка, стоимость, занятость, период
		// с длительностью и котировки оценки.
		var metrics = payload["metrics"]!;
		Assert.That(metrics["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(105.5m));
		Assert.That(metrics["totalPnLPercent"]!.GetValue<decimal>(), Is.EqualTo(3.5m));
		Assert.That(metrics["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(60.25m));
		Assert.That(metrics["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(40.25m));
		Assert.That(metrics["adjustmentsPnL"]!.GetValue<decimal>(), Is.EqualTo(5m));
		Assert.That(metrics["markValue"]!.GetValue<decimal>(), Is.EqualTo(500m));
		Assert.That(metrics["capitalUsagePercent"]!.GetValue<decimal>(), Is.EqualTo(16.7m));
		// Метрики карточки публикуют реальный риск для индикатора карточки:
		// величина проходит из метрик аналитики без пересчёта.
		// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
		Assert.That(metrics["realRiskUsdt"]!.GetValue<decimal>(), Is.EqualTo(150m));
		Assert.That(metrics["openedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-18T09:05:00+00:00"));
		Assert.That(metrics["closedAt"], Is.Null);
		Assert.That(metrics["durationSeconds"]!.GetValue<double>(), Is.EqualTo(53.4 * 3600).Within(0.1));
		Assert.That(payload["marksAsOf"]!.GetValue<string>(), Is.EqualTo("2026-06-20T14:30:00+00:00"));
		Assert.That(payload["hasMarkFailure"]!.GetValue<bool>(), Is.False);

		// Позиции: 15 колонок от входа до итога с комментарием и пометкой открытости.
		var position = payload["positions"]!.AsArray().Single(node => node!["symbol"]!.GetValue<string>() == "ETH-28JUN24-3200-C")!;
		Assert.That(position["residual"]!.GetValue<decimal>(), Is.EqualTo(2m));
		Assert.That(position["averageEntryPrice"]!.GetValue<decimal>(), Is.EqualTo(30m));
		Assert.That(position["averageClosePrice"], Is.Null);
		Assert.That(position["markValue"]!.GetValue<decimal>(), Is.EqualTo(300m));
		Assert.That(position["priceChangePercent"]!.GetValue<decimal>(), Is.EqualTo(5m));
		Assert.That(position["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(40m));
		Assert.That(position["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(30m));
		Assert.That(position["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(70m));
		Assert.That(position["accumulatedFees"]!.GetValue<decimal>(), Is.EqualTo(0.5m));
		Assert.That(position["openedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-18T09:05:00+00:00"));
		Assert.That(position["isOpen"]!.GetValue<bool>(), Is.True);
		Assert.That(position["comment"]!.GetValue<string>(), Is.EqualTo("ножка **входа**"));

		// Сделки: атрибуты биржевой записи и комментарий.
		var trade = payload["trades"]!.AsArray().Single(node => node!["execId"]!.GetValue<string>() == "exec-1")!;
		Assert.That(trade["symbol"]!.GetValue<string>(), Is.EqualTo("ETH-28JUN24-3200-C"));
		Assert.That(trade["executedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-18T09:05:00+00:00"));
		Assert.That(trade["isBuy"]!.GetValue<bool>(), Is.True);
		Assert.That(trade["quantity"]!.GetValue<decimal>(), Is.EqualTo(2m));
		Assert.That(trade["price"]!.GetValue<decimal>(), Is.EqualTo(30m));
		Assert.That(trade["amountUsdt"]!.GetValue<decimal>(), Is.EqualTo(60m));
		Assert.That(trade["fee"]!.GetValue<decimal>(), Is.EqualTo(0.1m));
		Assert.That(trade["comment"], Is.Null);

		// Закрывающие записи: единый поток с типом и ключом ручной пометки.
		var entry = payload["closingEntries"]!.AsArray().Single()!;
		Assert.That(entry["closedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-19T10:00:00+00:00"));
		Assert.That(entry["kind"]!.GetValue<string>(), Is.EqualTo("manual-mark"));
		Assert.That(entry["symbol"]!.GetValue<string>(), Is.EqualTo("ETHUSDT"));
		Assert.That(entry["quantity"]!.GetValue<decimal>(), Is.EqualTo(-0.5m));
		Assert.That(entry["price"]!.GetValue<decimal>(), Is.EqualTo(3520m));
		Assert.That(entry["amountUsdt"]!.GetValue<decimal>(), Is.EqualTo(-1760m));
		Assert.That(entry["manualMarkId"]!.GetValue<long>(), Is.EqualTo(21L));

		// Предупреждение об избыточной закрывающей записи доходит снимком.
		var warning = payload["closingWarnings"]!.AsArray().Single()!;
		Assert.That(warning["kind"]!.GetValue<string>(), Is.EqualTo("manual-mark"));
		Assert.That(warning["symbol"]!.GetValue<string>(), Is.EqualTo("ETHUSDT"));

		// Корректировки: пять колонок со знаковой суммой и источником.
		var adjustment = payload["adjustments"]!.AsArray().Single()!;
		Assert.That(adjustment["adjustmentId"]!.GetValue<long>(), Is.EqualTo(11L));
		Assert.That(adjustment["date"]!.GetValue<string>(), Is.EqualTo("2026-06-19T00:00:00+00:00"));
		Assert.That(adjustment["description"]!.GetValue<string>(), Is.EqualTo("PnL робота grid-ETH"));
		Assert.That(adjustment["source"]!.GetValue<string>(), Is.EqualTo("robot"));
		Assert.That(adjustment["amountUsdt"]!.GetValue<decimal>(), Is.EqualTo(5m));
	}

	[TestMethod]
	[Description("Снимок неизвестной конструкции отвечает 404")]
	// Чужой или удалённый идентификатор — явный 404: карточка показывает
	// состояние «не найдена» вместо пустых таблиц.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	public async Task ThrowOnUnknownConstructionCardReturns404()
	{
		// Arrange: стабильная read-модель, знающая только конструкцию 7.
		await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(new StubCardDetailReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос карточки неизвестной конструкции.
		var response = await client.GetAsync("/api/v1/constructions/999");

		// Assert: эндпоинт отвечает 404.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[TestMethod]
	[Description("Повреждённое сырьё журнала отвечает 503 с причиной")]
	// Явное состояние недоступности вместо пустой карточки: журналу не дали
	// собрать данные — пользователь видит причину обновить экран.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	public async Task ThrowOnBrokenJournalReturns503WithReason()
	{
		// Arrange: read-модель, падающая материализацией сделок.
		await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(StubCardDetailReadModel.WithBrokenJournal()));
		using var client = factory.CreateClient();

		// Act: запрос снимка карточки.
		var response = await client.GetAsync("/api/v1/constructions/7");

		// Assert: карточка отвечает 503 с причиной недоступности.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["error"]!.GetValue<string>(), Does.Contain("исполнени"));
	}

		[TestMethod]
		[Description("Действия шапки выполняются командами и доходят сервису домена")]
		// Действия конструкции живут в шапке карточки (паритет №5): переименование,
		// смена ручного статуса с возвратом из архива (№28), капитал с пустым
		// сохранением-удалением, риск/профит парой «значение + единица» и удаление
		// пустой с опциональной резервной копией — команды идут через единый API.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		public async Task TryIfHeaderActionsRunDomainCommands()
		{
			// Arrange: записывающий сервис конструкций.
			var constructions = new RecordingConstructionService();
			await using var factory = new SectionApiFactory(services => services.ReplaceConstructionService(constructions));
			using var client = factory.CreateClient();

			// Act: команды шапки по очереди.
			var rename = await client.PostAsJsonAsync("/api/v1/constructions/7/rename", new { name = "новое имя" });
			var status = await client.PostAsJsonAsync("/api/v1/constructions/7/status", new { status = "archived" });
			var restore = await client.PostAsJsonAsync("/api/v1/constructions/7/status", new { status = "closed" });
			var capital = await client.PostAsJsonAsync("/api/v1/constructions/7/capital", new { allocatedCapitalUsdt = (decimal?)null });
			var risk = await client.PostAsJsonAsync("/api/v1/constructions/7/risk", new { value = (decimal?)3.5m, unit = "percent" });
			var profit = await client.PostAsJsonAsync("/api/v1/constructions/7/profit", new { value = (decimal?)null, unit = (string?)null });
			var delete = await client.PostAsJsonAsync("/api/v1/constructions/7/delete", new { makeBackup = true });

			// Assert: команды приняты без тела и дошли сервису домена.
			Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(restore.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(capital.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(risk.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(profit.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(constructions.Renames, Is.EqualTo(new[] { (7L, "новое имя") }));
			Assert.That(constructions.StatusChanges, Is.EqualTo(new[] { (7L, ConstructionStatus.Archived), (7L, ConstructionStatus.Closed) }));
			Assert.That(constructions.Capitals, Is.EqualTo(new[] { (7L, (decimal?)null) }));
			Assert.That(constructions.Risks, Is.EqualTo(new[] { (7L, 3.5m, (TargetUnit?)TargetUnit.Percent) }));
			Assert.That(constructions.Profits, Is.EqualTo(new[] { (7L, (decimal?)null, (TargetUnit?)null) }));
			Assert.That(constructions.DeletedIds, Is.EqualTo(new[] { 7L }));
		}

		[TestMethod]
		[Description("Отказ удаления домена доходит причиной в 409")]
		// Отказы домена — причиной у действия: непустая конструкция не удаляется,
		// пользователь видит блокирующие записи, данные остаются неизменными.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		public async Task ThrowOnDeleteRefusalReturns409WithReason()
		{
			// Arrange: сервис, отказывающий удаление непустой конструкции.
			var constructions = RecordingConstructionService.RefusingDelete();
			await using var factory = new SectionApiFactory(services => services.ReplaceConstructionService(constructions));
			using var client = factory.CreateClient();

			// Act: команда удаления непустой конструкции.
			var response = await client.PostAsJsonAsync("/api/v1/constructions/7/delete", new { makeBackup = false });

			// Assert: отказ домена отвечает 409 с причиной.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
			var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
			Assert.That(payload!["error"]!.GetValue<string>(), Does.Contain("сделок"));
		}

		[TestMethod]
		[Description("Неудача резервной копии отменяет удаление с причиной в 503")]
		// Резервная копия предшествует команде домена: неудача копирования отменяет
		// удаление без обращения к сервису — данные конструкции нетронуты.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		public async Task ThrowOnBackupFailureCancelsDeleteWith503()
		{
			// Arrange: сервисы с падающим бэкапом; сервис конструкций записывает вызовы.
			var constructions = new RecordingConstructionService();
			await using var factory = new SectionApiFactory(services => services
				.ReplaceConstructionService(constructions)
				.ReplaceBackupService(StubBackupService.Failing()));
			using var client = factory.CreateClient();

			// Act: команда удаления с флажком резервной копии.
			var response = await client.PostAsJsonAsync("/api/v1/constructions/7/delete", new { makeBackup = true });

			// Assert: удаление отменено причиной, домен не вызывался.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
			var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
			Assert.That(payload!["error"]!.GetValue<string>(), Does.Contain("копия"));
			Assert.That(constructions.DeletedIds, Is.Empty);
		}

		[TestMethod]
		[Description("Действие неизвестной конструкции отвечает 404, пустое имя — 400")]
		// Команды шапки проверяют существование конструкции и корректность ввода:
		// исчезнувшая конструкция — 404, пустое имя — ошибка заполнения поля.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		public async Task ThrowOnHeaderActionValidationReturns404And400()
		{
			// Arrange: записывающий сервис конструкций.
			var constructions = new RecordingConstructionService();
			await using var factory = new SectionApiFactory(services => services.ReplaceConstructionService(constructions));
			using var client = factory.CreateClient();

			// Act: команда по неизвестной конструкции и переименование в пустое имя.
			var unknown = await client.PostAsJsonAsync("/api/v1/constructions/999/status", new { status = "closed" });
			var empty = await client.PostAsJsonAsync("/api/v1/constructions/7/rename", new { name = "  " });

			// Assert: неизвестная — 404, некорректный ввод — 400.
			Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		}

		[TestMethod]
		[Description("Комментарии трёх уровней сохраняются командой и очищаются пустым текстом")]
		// Комментарии всех трёх уровней — конструкции, позиции и сделки — правятся
		// по месту отображения модальным split-редактором (№6): сохранение идёт
		// командой через единый API, null или пробелы снимают комментарий.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		public async Task TryIfCommentsOfAllLevelsAreSetAndCleared()
		{
			// Arrange: записывающий сервис комментариев.
			var comments = new RecordingCommentService();
			await using var factory = new SectionApiFactory(services => services.ReplaceCommentService(comments));
			using var client = factory.CreateClient();

			// Act: сохранение комментария конструкции, позиции и сделки; очистка пробелами.
			var construction = await client.PutAsJsonAsync("/api/v1/constructions/7/comment", new { text = "итог **недели**" });
			var position = await client.PutAsJsonAsync("/api/v1/constructions/7/positions/ETH-28JUN24-3200-C/comment", new { text = "ножка входа" });
			var trade = await client.PutAsJsonAsync("/api/v1/trades/exec-1/comment", new { text = (string?)null });
			var cleared = await client.PutAsJsonAsync("/api/v1/constructions/7/comment", new { text = "   " });

			// Assert: команды приняты, сервис получил тексты; пустой текст очищает.
			Assert.That(construction.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(position.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(trade.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(cleared.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(comments.ConstructionComments, Is.EqualTo(new[] { (7L, "итог **недели**"), (7L, (string?)null) }));
			Assert.That(comments.PositionComments, Is.EqualTo(new[] { (7L, "ETH-28JUN24-3200-C", "ножка входа") }));
			Assert.That(comments.TradeComments, Is.EqualTo(new[] { ("exec-1", (string?)null) }));
		}

		[TestMethod]
		[Description("Комментарий с пустым ключом записи отвечает 400")]
		// Ключ уровня обязателен: пустой инструмент или execId — ошибка контракта,
		// команда в домен не проходит.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		public async Task ThrowOnEmptyCommentKeyReturns400()
		{
			// Arrange: записывающий сервис комментариев.
			var comments = new RecordingCommentService();
			await using var factory = new SectionApiFactory(services => services.ReplaceCommentService(comments));
			using var client = factory.CreateClient();

			// Act: команда комментария сделки с пустым ключом.
			var response = await client.PutAsJsonAsync("/api/v1/trades/%20/comment", new { text = "текст" });

			// Assert: эндпоинт отвечает 400.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		}

		[TestMethod]
		[Description("Ручная пометка закрытия ставится, правится и удаляется командами")]
		// Ручные пометки закрытия — из строки открытой позиции, с пикерами даты-времени
		// и дефолтом последней марки (№7): домен не проверяет остаток, избыточная
		// пометка станет предупреждением при чтении.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		public async Task TryIfManualCloseMarksAreAddedEditedAndDeleted()
		{
			// Arrange: записывающий сервис пометок.
			var marks = new RecordingCloseMarkService();
			await using var factory = new SectionApiFactory(services => services.ReplaceCloseMarkService(marks));
			using var client = factory.CreateClient();

			// Act: постановка пометки с ценой и без, правка и удаление.
			var add = await client.PostAsJsonAsync("/api/v1/constructions/7/close-marks", new
			{
				symbol = "ETH-28JUN24-3200-C",
				markedAt = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero),
				price = (decimal?)30.5m,
			});
			var addWithoutPrice = await client.PostAsJsonAsync("/api/v1/constructions/7/close-marks", new
			{
				symbol = "ETHUSDT",
				markedAt = new DateTimeOffset(2026, 6, 21, 11, 0, 0, TimeSpan.Zero),
				price = (decimal?)null,
			});
			var edit = await client.PutAsJsonAsync("/api/v1/close-marks/21", new
			{
				symbol = "ETH-28JUN24-3200-C",
				markedAt = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero),
				price = (decimal?)31m,
			});
			var delete = await client.DeleteAsync("/api/v1/close-marks/21");

			// Assert: команды приняты и дошли сервису домена.
			Assert.That(add.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(addWithoutPrice.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(edit.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(marks.Added, Has.Count.EqualTo(2));
			Assert.That(marks.Added[0], Is.EqualTo((7L, "ETH-28JUN24-3200-C", new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero), 30.5m)));
			Assert.That(marks.Added[1].Price, Is.Null);
			Assert.That(marks.Edited, Is.EqualTo(new[] { (21L, "ETH-28JUN24-3200-C", new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero), 31m) }));
			Assert.That(marks.DeletedIds, Is.EqualTo(new[] { 21L }));
		}

		[TestMethod]
		[Description("Последняя марка инструмента отдаётся для предзаполнения формы пометки")]
		// Форма пометки закрытия открывается с дефолтом последней известной марки
		// инструмента: предзаполнение спрашивает API, а не хранит marcas в SPA.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		public async Task TryIfLastMarkServesFormPrefill()
		{
			// Arrange: источник марок со стабильной величиной.
			await using var factory = new SectionApiFactory(services => services.ReplaceInstrumentMarkSource(new StubInstrumentMarkSource(30.5m)));
			using var client = factory.CreateClient();

			// Act: запрос последней марки инструмента.
			var response = await client.GetAsync("/api/v1/constructions/7/positions/ETH-28JUN24-3200-C/last-mark");

			// Assert: марка приходит формой предзаполнения.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
			Assert.That(payload!["mark"]!.GetValue<decimal>(), Is.EqualTo(30.5m));
		}

		[TestMethod]
		[Description("Цели переноса сделки перечисляют активные конструкции без текущей")]
		// Форма «Перенести…» строки сделки выбирает целевую конструкцию из активных
		// без текущей (№8): архивные скрыты, самому себе переносить нечего.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		public async Task TryIfMoveTargetsListActiveConstructionsWithoutCurrent()
		{
			// Arrange: сервис конструкций с активным списком из двух записей.
			var constructions = RecordingConstructionService.WithActiveList(
				(7L, "ETH-240628-3200C+P", ConstructionStatus.Open),
				(8L, "BTC-240531-60000C", ConstructionStatus.Closed));
			await using var factory = new SectionApiFactory(services => services.ReplaceConstructionService(constructions));
			using var client = factory.CreateClient();

			// Act: запрос целей переноса из конструкции 7.
			var response = await client.GetAsync("/api/v1/constructions/7/move-targets");

			// Assert: единственная цель — закрытая конструкция 8 без метрик.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
			var targets = payload!["targets"]!.AsArray();
			Assert.That(targets, Has.Count.EqualTo(1));
			Assert.That(targets[0]!["constructionId"]!.GetValue<long>(), Is.EqualTo(8L));
			Assert.That(targets[0]!["name"]!.GetValue<string>(), Is.EqualTo("BTC-240531-60000C"));
		}

		[TestMethod]
		[Description("Возврат сделки во «Входящие» и перенос выполняются командами")]
		// Действия строки сделки (№8): возврат во «Входящие» снимает привязку,
		// сохраняя комментарий; перенос назначает целевую конструкцию — команды
		// меняют только принадлежность, производные пересчитываются при чтении.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		public async Task TryIfTradeReturnAndMoveRunBindingCommands()
		{
			// Arrange: записывающий сервис привязок.
			var bindings = new RecordingTradeBindingService();
			await using var factory = new SectionApiFactory(services => services.ReplaceTradeBindingService(bindings));
			using var client = factory.CreateClient();

			// Act: возврат во «Входящие» и перенос в конструкцию 8.
			var returned = await client.PostAsync("/api/v1/trades/exec-1/return-to-inbox", content: null);
			var moved = await client.PostAsJsonAsync("/api/v1/trades/exec-2/move", new { constructionId = 8L });

			// Assert: команды приняты и дошли сервису домена.
			Assert.That(returned.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(moved.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(bindings.Unbound, Is.EqualTo(new[] { "exec-1" }));
			Assert.That(bindings.Moved, Is.EqualTo(new[] { (8L, "exec-2") }));
		}

		[TestMethod]
		[Description("Перенос с пустым ключом сделки или целью отвечает 400")]
		// Ключ сделки и целевая конструкция обязательны: пустые значения — ошибка
		// контракта, привязка не меняется.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		public async Task ThrowOnTradeActionValidationReturns400()
		{
			// Arrange: записывающий сервис привязок.
			var bindings = new RecordingTradeBindingService();
			await using var factory = new SectionApiFactory(services => services.ReplaceTradeBindingService(bindings));
			using var client = factory.CreateClient();

			// Act: перенос с пустым ключом сделки.
			var response = await client.PostAsJsonAsync("/api/v1/trades/%20/move", new { constructionId = 8L });

			// Assert: эндпоинт отвечает 400.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		}

		[TestMethod]
		[Description("Корректировки PnL добавляются, правятся и удаляются командами")]
		// Корректировки PnL живут в карточке (№9): форма добавления с датой,
		// источником и знаковой суммой, inline-правка и удаление — сводка
		// пересчитывается ближайшим чтением.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
		public async Task TryIfAdjustmentsAreAddedEditedAndDeleted()
		{
			// Arrange: записывающий сервис корректировок.
			var adjustments = new RecordingAdjustmentService();
			await using var factory = new SectionApiFactory(services => services.ReplacePnLAdjustmentService(adjustments));
			using var client = factory.CreateClient();

			// Act: добавление, правка и удаление корректировки.
			var add = await client.PostAsJsonAsync("/api/v1/constructions/7/adjustments", new
			{
				date = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
				source = "manual",
				amountUsdt = -12.5m,
				description = "перенос из робота",
			});
			var edit = await client.PutAsJsonAsync("/api/v1/adjustments/11", new
			{
				date = new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero),
				source = "robot",
				amountUsdt = 87.4m,
				description = (string?)null,
			});
			var delete = await client.DeleteAsync("/api/v1/adjustments/11");

			// Assert: команды приняты и дошли сервису домена.
			Assert.That(add.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(edit.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(adjustments.Added, Is.EqualTo(new[] { (7L, new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero), PnLAdjustmentSource.Manual, -12.5m, "перенос из робота") }));
			Assert.That(adjustments.Edited, Is.EqualTo(new[] { (11L, new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero), PnLAdjustmentSource.Robot, 87.4m, (string?)null) }));
			Assert.That(adjustments.DeletedIds, Is.EqualTo(new[] { 11L }));
		}

		[TestMethod]
		[Description("Корректировка с неизвестным источником отвечает 400")]
		// Источник корректировки — закрытый набор «робот»/«ручная»: постороннее
		// значение — ошибка контракта, команда в домен не проходит.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		public async Task ThrowOnUnknownAdjustmentSourceReturns400()
		{
			// Arrange: записывающий сервис корректировок.
			var adjustments = new RecordingAdjustmentService();
			await using var factory = new SectionApiFactory(services => services.ReplacePnLAdjustmentService(adjustments));
			using var client = factory.CreateClient();

			// Act: добавление корректировки с неизвестным источником.
			var response = await client.PostAsJsonAsync("/api/v1/constructions/7/adjustments", new
			{
				date = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero),
				source = "external",
				amountUsdt = 1m,
				description = (string?)null,
			});

			// Assert: эндпоинт отвечает 400.
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
		}

		[TestMethod]
		[Description("OpenAPI-документ содержит эндпоинты карточки конструкции")]
		// Контракт карточки публикуется в OpenAPI: документ отражает фактические
		// эндпоинты, пригоден для ревью контрактов и генерации клиента SPA.
		// Traceability: openspec:http-api/transport#scenario-openapi-schema-published
		public async Task TryIfOpenApiListsCardEndpoints()
		{
			// Arrange: хост карточки со стабильными read-моделями.
			await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(new StubCardDetailReadModel()));
			using var client = factory.CreateClient();

			// Act: запрос OpenAPI-документа.
			var document = await (await client.GetAsync("/openapi/v1.json")).Content.ReadFromJsonAsync<JsonNode>();

			// Assert: пути карточки опубликованы под версионированным префиксом.
			var paths = document!["paths"]!.AsObject().Select(path => path.Key).ToArray();
			Assert.That(paths, Does.Contain("/api/v1/constructions/{constructionId}"));
			Assert.That(paths, Does.Contain("/api/v1/constructions/{constructionId}/rename"));
			Assert.That(paths, Does.Contain("/api/v1/constructions/{constructionId}/close-marks"));
			Assert.That(paths, Does.Contain("/api/v1/constructions/{constructionId}/move-targets"));
			Assert.That(paths, Does.Contain("/api/v1/constructions/{constructionId}/adjustments"));
			Assert.That(paths, Does.Contain("/api/v1/trades/{execId}/return-to-inbox"));
			Assert.That(paths, Does.Contain("/api/v1/adjustments/{adjustmentId}"));
		}
}

/// <summary>Записывающий сервис конструкций: фиксирует команды карточки.</summary>
internal sealed class RecordingConstructionService : IConstructionService
{
	/// <summary>Вызовы переименования: идентификатор и новое имя.</summary>
	public IReadOnlyList<(long ConstructionId, string Name)> Renames => _renames;

	/// <summary>Вызовы смены статуса: идентификатор и статус.</summary>
	public IReadOnlyList<(long ConstructionId, ConstructionStatus Status)> StatusChanges => _statusChanges;

	/// <summary>Вызовы правки капитала: идентификатор и значение (null — убрать).</summary>
	public IReadOnlyList<(long ConstructionId, decimal? Capital)> Capitals => _capitals;

	/// <summary>Вызовы правки риска: идентификатор, значение и единица.</summary>
	public IReadOnlyList<(long ConstructionId, decimal? Value, TargetUnit? Unit)> Risks => _risks;

	/// <summary>Вызовы правки профита: идентификатор, значение и единица.</summary>
	public IReadOnlyList<(long ConstructionId, decimal? Value, TargetUnit? Unit)> Profits => _profits;

	/// <summary>Идентификаторы удалённых конструкций.</summary>
	public IReadOnlyList<long> DeletedIds => _deletedIds;

	private readonly List<(long, string)> _renames = [];

	private readonly List<(long, ConstructionStatus)> _statusChanges = [];

	private readonly List<(long, decimal?)> _capitals = [];

	private readonly List<(long, decimal?, TargetUnit?)> _risks = [];

	private readonly List<(long, decimal?, TargetUnit?)> _profits = [];

	private readonly List<long> _deletedIds = [];

	private readonly bool _refuseDelete;

	private readonly IReadOnlyList<Construction> _active;

	private RecordingConstructionService(bool refuseDelete, IReadOnlyList<Construction> active)
	{
		_refuseDelete = refuseDelete;
		_active = active;
	}

	/// <summary>Обычная запись команд без активного списка.</summary>
	public RecordingConstructionService()
		: this(refuseDelete: false, active: [])
	{
	}

	/// <summary>Вариант с активным списком конструкций для целей переноса.</summary>
	public static RecordingConstructionService WithActiveList(params (long Id, string Name, ConstructionStatus Status)[] constructions) =>
		new(refuseDelete: false, active: constructions.Select(item => new Construction { Id = item.Id, Name = item.Name, Status = item.Status }).ToArray());

	/// <summary>Вариант с отказом удаления: у конструкции есть блокирующие записи.</summary>
	public static RecordingConstructionService RefusingDelete() => new(refuseDelete: true, active: []);

	public Task<Construction> CreateAsync(string name, decimal? allocatedCapitalUsdt, string? comment = null, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task RenameAsync(long constructionId, string newName, CancellationToken cancellationToken = default)
	{
		EnsureKnown(constructionId);
		if (newName.Trim().Length == 0)
		{
			throw new ArgumentException("имя пусто", nameof(newName));
		}

		_renames.Add((constructionId, newName));
		return Task.CompletedTask;
	}

	public Task UpdateAllocatedCapitalAsync(long constructionId, decimal? allocatedCapitalUsdt, CancellationToken cancellationToken = default)
	{
		EnsureKnown(constructionId);
		_capitals.Add((constructionId, allocatedCapitalUsdt));
		return Task.CompletedTask;
	}

	public Task UpdateRiskAsync(long constructionId, decimal? value, TargetUnit? unit, CancellationToken cancellationToken = default)
	{
		EnsureKnown(constructionId);
		_risks.Add((constructionId, value, unit));
		return Task.CompletedTask;
	}

	public Task UpdateProfitAsync(long constructionId, decimal? value, TargetUnit? unit, CancellationToken cancellationToken = default)
	{
		EnsureKnown(constructionId);
		_profits.Add((constructionId, value, unit));
		return Task.CompletedTask;
	}

	public Task ChangeStatusAsync(long constructionId, ConstructionStatus status, CancellationToken cancellationToken = default)
	{
		EnsureKnown(constructionId);
		_statusChanges.Add((constructionId, status));
		return Task.CompletedTask;
	}

	public Task ArchiveAsync(long constructionId, CancellationToken cancellationToken = default) =>
		ChangeStatusAsync(constructionId, ConstructionStatus.Archived, cancellationToken);

	public Task DeleteAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		EnsureKnown(constructionId);
		if (_refuseDelete)
		{
			throw new ConstructionDeletionRefusedException(boundTradeCount: 2, adjustmentCount: 0);
		}

		_deletedIds.Add(constructionId);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<Construction>> ListActiveAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult(_active);

	public Task<IReadOnlyList<Construction>> ListAllAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<Construction>>([]);

	private void EnsureKnown(long constructionId)
	{
		if (constructionId != 7L)
		{
			throw new ConstructionNotFoundException(constructionId);
		}
	}
}

/// <summary>Стабильная заглушка сервиса резервных копий: успех или неудача копии.</summary>
internal sealed class StubBackupService : IJournalBackupService
{
	private readonly bool _failing;

	private StubBackupService(bool failing)
	{
		_failing = failing;
	}

	/// <summary>Успешная копия с пустым результатом ротации.</summary>
	public static StubBackupService Succeeding() => new(failing: false);

	/// <summary>Неудача копии: IOException отменяет операцию вызывающего.</summary>
	public static StubBackupService Failing() => new(failing: true);

	public Task<JournalBackupResult> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
	{
		if (_failing)
		{
			throw new IOException("копия не создана");
		}

		return Task.FromResult(new JournalBackupResult { FileName = $"journal-{reason}.db", RotationWarnings = [] });
	}
}

/// <summary>Записывающий сервис комментариев: фиксирует правки трёх уровней.</summary>
internal sealed class RecordingCommentService : ICommentService
{
	/// <summary>Правки комментария конструкции: идентификатор и текст (null — снят).</summary>
	public IReadOnlyList<(long ConstructionId, string? Comment)> ConstructionComments => _constructionComments;

	/// <summary>Правки комментария позиции: конструкция, инструмент и текст.</summary>
	public IReadOnlyList<(long ConstructionId, string Symbol, string? Text)> PositionComments => _positionComments;

	/// <summary>Правки комментария сделки: execId и текст.</summary>
	public IReadOnlyList<(string ExecId, string? Comment)> TradeComments => _tradeComments;

	private readonly List<(long, string?)> _constructionComments = [];

	private readonly List<(long, string, string?)> _positionComments = [];

	private readonly List<(string, string?)> _tradeComments = [];

	public Task SetTradeCommentAsync(string execId, string? comment, CancellationToken cancellationToken = default)
	{
		EnsureKey(execId);
		_tradeComments.Add((execId, comment));
		return Task.CompletedTask;
	}

	public Task SetPositionCommentAsync(long constructionId, string symbol, string? text, CancellationToken cancellationToken = default)
	{
		EnsureKey(symbol);
		_positionComments.Add((constructionId, symbol, text));
		return Task.CompletedTask;
	}

	public Task SetConstructionCommentAsync(long constructionId, string? comment, CancellationToken cancellationToken = default)
	{
		_constructionComments.Add((constructionId, comment));
		return Task.CompletedTask;
	}

	private static void EnsureKey(string key)
	{
		if (key.Trim().Length == 0)
		{
			throw new ArgumentException("ключ пуст");
		}
	}
}

/// <summary>Записывающий сервис привязки сделок: фиксирует команды карточки.</summary>
internal sealed class RecordingTradeBindingService : ITradeBindingService
{
	/// <summary>Возвращённые во «Входящие» сделки по execId.</summary>
	public IReadOnlyList<string> Unbound => _unbound;

	/// <summary>Переносы сделок: целевая конструкция и execId.</summary>
	public IReadOnlyList<(long ConstructionId, string ExecId)> Moved => _moved;

	private readonly List<string> _unbound = [];

	private readonly List<(long, string)> _moved = [];

	public Task BindAsync(long constructionId, string execId, CancellationToken cancellationToken = default)
	{
		EnsureExecId(execId);
		_moved.Add((constructionId, execId));
		return Task.CompletedTask;
	}

	public Task BindBatchAsync(long constructionId, IEnumerable<string> execIds, CancellationToken cancellationToken = default)
	{
		foreach (var execId in execIds)
		{
			BindAsync(constructionId, execId, cancellationToken);
		}

		return Task.CompletedTask;
	}

	public Task UnbindAsync(string execId, CancellationToken cancellationToken = default)
	{
		EnsureExecId(execId);
		_unbound.Add(execId);
		return Task.CompletedTask;
	}

	private static void EnsureExecId(string execId)
	{
		if (execId.Trim().Length == 0)
		{
			throw new ArgumentException("ключ пуст");
		}
	}
}

/// <summary>Записывающий сервис корректировок PnL: фиксирует команды карточки.</summary>
internal sealed class RecordingAdjustmentService : IPnLAdjustmentService
{
	/// <summary>Добавленные корректировки: конструкция, дата, источник, сумма и комментарий.</summary>
	public IReadOnlyList<(long ConstructionId, DateTimeOffset Date, PnLAdjustmentSource Source, decimal Amount, string? Comment)> Added => _added;

	/// <summary>Правки корректировок: ключ, дата, источник, сумма и комментарий.</summary>
	public IReadOnlyList<(long AdjustmentId, DateTimeOffset Date, PnLAdjustmentSource Source, decimal Amount, string? Comment)> Edited => _edited;

	/// <summary>Удалённые корректировки.</summary>
	public IReadOnlyList<long> DeletedIds => _deletedIds;

	private readonly List<(long, DateTimeOffset, PnLAdjustmentSource, decimal, string?)> _added = [];

	private readonly List<(long, DateTimeOffset, PnLAdjustmentSource, decimal, string?)> _edited = [];

	private readonly List<long> _deletedIds = [];

	public Task<PnLAdjustment> AddAsync(long constructionId, DateTimeOffset date, PnLAdjustmentSource source, decimal amountUsdt, string? comment = null, CancellationToken cancellationToken = default)
	{
		_added.Add((constructionId, date, source, amountUsdt, comment));
		return Task.FromResult(new PnLAdjustment { ConstructionId = constructionId, Date = date, Source = source, AmountUsdt = amountUsdt, Comment = comment });
	}

	public Task EditAsync(long adjustmentId, DateTimeOffset date, PnLAdjustmentSource source, decimal amountUsdt, string? comment, CancellationToken cancellationToken = default)
	{
		_edited.Add((adjustmentId, date, source, amountUsdt, comment));
		return Task.CompletedTask;
	}

	public Task DeleteAsync(long adjustmentId, CancellationToken cancellationToken = default)
	{
		_deletedIds.Add(adjustmentId);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<PnLAdjustment>> ListAsync(long constructionId, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<PnLAdjustment>>([]);
}

/// <summary>Записывающий сервис ручных пометок закрытия: фиксирует команды карточки.</summary>
internal sealed class RecordingCloseMarkService : IManualCloseMarkService
{
	/// <summary>Поставленные пометки: конструкция, инструмент, время и цена.</summary>
	public IReadOnlyList<(long ConstructionId, string Symbol, DateTimeOffset MarkedAt, decimal? Price)> Added => _added;

	/// <summary>Правки пометок: ключ, инструмент, время и цена.</summary>
	public IReadOnlyList<(long MarkId, string Symbol, DateTimeOffset MarkedAt, decimal? Price)> Edited => _edited;

	/// <summary>Удалённые пометки.</summary>
	public IReadOnlyList<long> DeletedIds => _deletedIds;

	private readonly List<(long, string, DateTimeOffset, decimal?)> _added = [];

	private readonly List<(long, string, DateTimeOffset, decimal?)> _edited = [];

	private readonly List<long> _deletedIds = [];

	public Task<ManualCloseMark> AddAsync(long constructionId, string symbol, DateTimeOffset markedAt, decimal? price = null, CancellationToken cancellationToken = default)
	{
		_added.Add((constructionId, symbol, markedAt, price));
		return Task.FromResult(new ManualCloseMark { ConstructionId = constructionId, Symbol = symbol, Price = price, MarkedAt = markedAt });
	}

	public Task EditAsync(long markId, string symbol, DateTimeOffset markedAt, decimal? price, CancellationToken cancellationToken = default)
	{
		_edited.Add((markId, symbol, markedAt, price));
		return Task.CompletedTask;
	}

	public Task DeleteAsync(long markId, CancellationToken cancellationToken = default)
	{
		_deletedIds.Add(markId);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<ManualCloseMark>> ListAsync(long constructionId, CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<ManualCloseMark>>([]);
}

/// <summary>Стабильный источник марок: одна известная марка инструмента.</summary>
internal sealed class StubInstrumentMarkSource(decimal? mark) : IInstrumentMarkSource
{
	public Task<decimal?> GetLastMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
		Task.FromResult(mark);
}

/// <summary>Помощники подмены сервисов карточки стабильными заглушками.</summary>
internal static class CardApiServiceExtensions
{
	/// <summary>Заменяет регистрацию сервиса конструкций стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceConstructionService(this IServiceCollection services, IConstructionService constructionService)
	{
		return Replace(services, constructionService);
	}

	/// <summary>Заменяет регистрацию сервиса резервных копий стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceBackupService(this IServiceCollection services, IJournalBackupService backupService)
	{
		return Replace(services, backupService);
	}

	/// <summary>Заменяет регистрацию сервиса комментариев стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceCommentService(this IServiceCollection services, ICommentService commentService)
	{
		return Replace(services, commentService);
	}

	/// <summary>Заменяет регистрацию сервиса ручных пометок стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceCloseMarkService(this IServiceCollection services, IManualCloseMarkService markService)
	{
		return Replace(services, markService);
	}

	/// <summary>Заменяет регистрацию сервиса привязки сделок стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceTradeBindingService(this IServiceCollection services, ITradeBindingService bindingService)
	{
		return Replace(services, bindingService);
	}

	/// <summary>Заменяет регистрацию сервиса корректировок стабильной заглушкой.</summary>
	public static IServiceCollection ReplacePnLAdjustmentService(this IServiceCollection services, IPnLAdjustmentService adjustmentService)
	{
		return Replace(services, adjustmentService);
	}

	/// <summary>Заменяет регистрацию источника марок стабильной заглушкой.</summary>
	public static IServiceCollection ReplaceInstrumentMarkSource(this IServiceCollection services, IInstrumentMarkSource markSource)
	{
		return Replace(services, markSource);
	}

	private static IServiceCollection Replace<TService>(IServiceCollection services, TService replacement)
	{
		foreach (var existing in services.Where(service => service.ServiceType == typeof(TService)).ToArray())
		{
			services.Remove(existing);
		}

		services.Add(ServiceDescriptor.Singleton(typeof(TService), replacement));
		return services;
	}
}

/// <summary>
/// Стабильная read-модель деталей для карточки: конструкция 7 с открытым
/// остатком, двумя позициями, тремя сделками, ручной закрывающей записью,
/// предупреждением об избыточной записи и одной корректировкой.
/// </summary>
internal sealed class StubCardDetailReadModel : IConstructionDetailReadModel
{
	private readonly bool _brokenJournal;

	private StubCardDetailReadModel(bool brokenJournal)
	{
		_brokenJournal = brokenJournal;
	}

	/// <summary>Вариант по умолчанию: конструкция 7 со всеми таблицами записей.</summary>
	public StubCardDetailReadModel()
		: this(brokenJournal: false)
	{
	}

	/// <summary>Вариант с повреждённым сырьём: чтение падает материализацией сделок.</summary>
	public static StubCardDetailReadModel WithBrokenJournal() => new(brokenJournal: true);

	public Task<ConstructionDetailData> ReadAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		if (_brokenJournal)
		{
			throw new TradeMaterializationException("exec-1", "сырая запись исполнения повреждена");
		}

		if (constructionId != 7L)
		{
			throw new ConstructionNotFoundException(constructionId);
		}

		var marksAsOf = new DateTimeOffset(2026, 6, 20, 14, 30, 0, TimeSpan.Zero);
		var openedAt = new DateTimeOffset(2026, 6, 18, 9, 5, 0, TimeSpan.Zero);
		var closedPositionAt = new DateTimeOffset(2026, 6, 19, 10, 0, 0, TimeSpan.Zero);
		var metrics = new TransactionJournal.Application.Analytics.ConstructionMetrics
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
			Comment: "## стреддл\n*имя ручное*",
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
					Comment: "ножка **входа**",
					MarkValue: 300m,
					PriceChangePercent: 5m),
				new ConstructionPositionRow(
					Symbol: "ETHUSDT",
					Residual: 0m,
					AverageEntryPrice: 3500m,
					AverageClosePrice: 3520m,
					RealizedPnL: 20.25m,
					RealizedPnLPercent: 0.67m,
					UnrealizedPnL: null,
					UnrealizedPnLPercent: null,
					TotalPnL: 20.25m,
					TotalPnLPercent: 0.67m,
					AccumulatedFees: 0.25m,
					OpenedAt: openedAt,
					ClosedAt: closedPositionAt,
					IsOpen: false,
					Comment: null,
					MarkValue: null,
					PriceChangePercent: null),
			],
			Trades:
			[
				new ConstructionTradeRow("exec-1", "ETH-28JUN24-3200-C", openedAt, true, 2m, 30m, 60m, 0.1m, null),
				new ConstructionTradeRow("exec-2", "ETHUSDT", openedAt.AddHours(1), true, 0.5m, 3500m, 1750m, 0.2m, null),
				new ConstructionTradeRow("exec-3", "ETHUSDT", openedAt.AddHours(2), false, 0.5m, 3520m, 1760m, 0.15m, null),
			],
			ClosingEntries:
			[
				new ConstructionClosingEntryRow(closedPositionAt, PositionClosingKind.ManualMark, "ETHUSDT", -0.5m, 3520m, -1760m, ManualMarkId: 21),
			],
			ClosingWarnings:
			[
				new RedundantClosingEntryWarning
				{
					ConstructionId = constructionId,
					Symbol = "ETHUSDT",
					Kind = PositionClosingKind.ManualMark,
					ClosedAt = closedPositionAt,
					SourceKey = "manual:99",
				},
			],
			Adjustments:
			[
				new ConstructionAdjustmentRow(11, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero), "PnL робота grid-ETH", PnLAdjustmentSource.Robot, 5m),
			],
			RiskUnit: TargetUnit.Percent,
			ProfitUnit: TargetUnit.Usdt);
		return Task.FromResult(data);
	}
}
