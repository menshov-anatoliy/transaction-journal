using TransactionJournal.Domain.Bybit;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application.Sync;
using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.ManualTrades;

/// <summary>
/// Разрешение символа ручной сделки: сверяет символ со справочником инструментов,
/// а отсутствующий символ пополняет спецификацией публичного эндпоинта
/// instruments-info тем же путём, что и InstrumentReferenceSync, — без API-ключей.
/// Известный символ разрешается без сетевых запросов; неизвестный запрашивается
/// у биржи фильтром по символу и сохраняется в RawInstruments до принятия сделки.
/// Отказ сети или биржи оборачивается человекочитаемой ошибкой — создание сделки
/// с этим символом отменяется. Переиспользуемый шаг сервиса ручных сделок.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// Traceability: change:manual-journal-management/specs/sync/bybit-history/spec#scenario-manual-symbol-populates-catalog
/// Traceability: change:manual-journal-management/design#d3
/// </summary>
public sealed class ManualInstrumentResolver
{
	/// <summary>Категория линейных инструментов Bybit для символов вне формата опциона.</summary>
	public const string LinearCategory = "linear";

	private readonly IBybitInstrumentSource _source;
	private readonly IInstrumentReferenceStore _store;

	/// <summary>Создаёт разрешитель над источником спецификаций и хранилищем справочника.</summary>
	/// <param name="source">Публичный источник спецификаций instruments-info.</param>
	/// <param name="store">Хранилище сырых записей справочника.</param>
	/// <exception cref="ArgumentNullException">Источник или хранилище не заданы.</exception>
	public ManualInstrumentResolver(IBybitInstrumentSource source, IInstrumentReferenceStore store)
	{
		_source = source ?? throw new ArgumentNullException(nameof(source));
		_store = store ?? throw new ArgumentNullException(nameof(store));
	}

	/// <summary>
	/// Разрешает символ ручной сделки: сверяет его со справочником инструментов;
	/// отсутствующий символ запрашивается у биржи публичным эндпоинтом и сохраняется
	/// в сыром справочнике до возврата результата.
	/// </summary>
	/// <param name="symbol">Символ инструмента, например BTC-27DEC24-2800-C или BTCUSDT.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <returns>Категория символа и признак свежего пополнения справочника.</returns>
	/// <exception cref="ArgumentException">Символ не задан или состоит из пробелов.</exception>
	/// <exception cref="ManualSymbolResolutionException">Биржа недоступна, ответила ошибкой или не знает символ.</exception>
	public async Task<ManualSymbolResolution> ResolveAsync(string symbol, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		// Категория запроса выводится из формы символа: опцион узнаётся разбором
		// формата {BASE}-{dMMMyy}-{strike}-{C|P}[-{QUOTE}], остальные символы
		// адресуются линейной категории — как их хранит справочник синхронизации.
		// Traceability: change:manual-journal-management/design#d3
		var category = OptionSymbolParser.TryParse(symbol, out _)
			? InstrumentResolver.OptionsCategory
			: LinearCategory;

		// Сверка со справочником идёт по сырью, из которого строится снимок
		// InstrumentCatalog: известный символ разрешается без сетевых запросов.
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		var knownSymbols = await _store.FindKnownSymbolsAsync([symbol], cancellationToken).ConfigureAwait(false);
		if (knownSymbols.Contains(symbol))
		{
			return new ManualSymbolResolution { Symbol = symbol, Category = category, RegisteredNow = false };
		}

		BybitPagedResponse<BybitInstrumentInfo> page;
		try
		{
			// Фильтр по символу запрашивает спецификацию одного инструмента —
			// тот же публичный канал, которым справочник пополняет синхронизация.
			// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
			page = await _source.GetInstrumentInfoAsync(
				new BybitInstrumentInfoQuery { Category = category, Symbol = symbol },
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Отмена операции пользователем — не отказ пополнения, пробрасывается как есть.
			throw;
		}
		catch (Exception exception) when (exception is BybitApiException or HttpRequestException or TaskCanceledException)
		{
			// Отказ сети или биржи отменяет создание сделки человекочитаемой причиной:
			// пользователь должен понять, что сделка не сохранена и почему.
			// Traceability: change:manual-journal-management/design#d3
			throw new ManualSymbolResolutionException(
				symbol,
				$"Не удалось получить спецификацию инструмента «{symbol}» из биржи: {exception.Message} Сделка не сохранена; проверьте символ и доступ к сети и повторите.",
				exception);
		}

		// Пустой ответ на фильтр символа означает, что биржа такой инструмент не знает:
		// пополнять справочник нечем, а принятие сделки оставило бы её неразрешённой
		// материализацией — сделка отклоняется человекочитаемой причиной.
		var specification = page.List.FirstOrDefault(info => string.Equals(info.Symbol, symbol, StringComparison.Ordinal));
		if (specification is null)
		{
			throw new ManualSymbolResolutionException(
				symbol,
				$"Биржа не возвращает спецификацию инструмента «{symbol}». Сделка не сохранена; проверьте написание символа.");
		}

		// Спецификация сохраняется тем же путём, что и при синхронизации: сырая запись
		// справочника; счётчик запуска не ведётся — у ручного ввода нет строки SyncRun.
		// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
		await _store.WriteAsync(category, [specification], progressRun: null, cancellationToken).ConfigureAwait(false);
		return new ManualSymbolResolution { Symbol = symbol, Category = category, RegisteredNow = true };
	}
}
