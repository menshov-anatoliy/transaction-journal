using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Пополнение справочника инструментов: находит символы, встреченные в записях
/// синхронизации и отсутствующие в справочнике, запрашивает их спецификации
/// публичным эндпоинтом instruments-info и сохраняет сырые записи в хранилище —
/// до материализации сделок, чтобы сверка символа опциона опиралась на канонические
/// данные биржи, а не на разбор строки символа.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// Traceability: change:add-bybit-sync/design#d7
/// </summary>
public sealed class InstrumentReferenceSync
{
	private readonly IBybitInstrumentSource _source;
	private readonly IInstrumentReferenceStore _store;

	/// <summary>Создаёт пополнитель справочника над источником спецификаций и хранилищем.</summary>
	/// <param name="source">Источник спецификаций instruments-info.</param>
	/// <param name="store">Хранилище сырых записей справочника.</param>
	/// <exception cref="ArgumentNullException">Источник или хранилище не заданы.</exception>
	public InstrumentReferenceSync(IBybitInstrumentSource source, IInstrumentReferenceStore store)
	{
		_source = source ?? throw new ArgumentNullException(nameof(source));
		_store = store ?? throw new ArgumentNullException(nameof(store));
	}

	/// <summary>
	/// Пополняет справочник неизвестными символами из записей синхронизации: для каждой
	/// пары категория-символ, отсутствующей в хранилище, запрашивает спецификацию биржи
	/// фильтром по символу и сохраняет её в сыром виде, продвигая счётчик инструментов запуска.
	/// </summary>
	/// <param name="instruments">Пары категория-символ из новых записей исполнения и delivery-записей.</param>
	/// <param name="progressRun">Запуск, чей счётчик новых инструментов продвигается; null — прогресс не ведётся.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <returns>Число фактически вставленных спецификаций.</returns>
	/// <exception cref="ArgumentNullException">Коллекция пар не задана.</exception>
	/// <exception cref="ArgumentException">Категория или символ какой-либо пары не заданы.</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов.</exception>
	public async Task<int> SyncAsync(
		IReadOnlyCollection<(string Category, string Symbol)> instruments,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(instruments);

		// Пары дедуплицируются до обращения к хранилищу и бирже: один инструмент
		// встречается в записях многократно, но спецификация нужна одна.
		var uniquePairs = new HashSet<(string Category, string Symbol)>();
		foreach (var (category, symbol) in instruments)
		{
			if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(symbol))
			{
				throw new ArgumentException(
					"Категория и символ инструмента в записях синхронизации должны быть заданы.",
					nameof(instruments));
			}

			uniquePairs.Add((category, symbol));
		}

		if (uniquePairs.Count == 0)
		{
			return 0;
		}

		// Неизвестными остаются только символы, которых нет в справочнике: повторные синки
		// не перечитывают спецификации уже известных инструментов.
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		var knownSymbols = await _store.FindKnownSymbolsAsync(
			uniquePairs.Select(pair => pair.Symbol).ToArray(), cancellationToken).ConfigureAwait(false);
		var unknownPairs = uniquePairs
			.Where(pair => knownSymbols.Contains(pair.Symbol) == false)
			.ToList();

		var insertedCount = 0;
		foreach (var categoryGroups in unknownPairs.GroupBy(pair => pair.Category, StringComparer.Ordinal))
		{
			var fetched = new List<BybitInstrumentInfo>();
			foreach (var pair in categoryGroups)
			{
				cancellationToken.ThrowIfCancellationRequested();

				// Фильтр по символу запрашивает спецификацию одного инструмента: биржа
				// отвечает одной страницей без курсора продолжения.
				// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
				var page = await _source.GetInstrumentInfoAsync(
					new BybitInstrumentInfoQuery { Category = categoryGroups.Key, Symbol = pair.Symbol },
					cancellationToken).ConfigureAwait(false);

				// Ответ фильтра сверяется с запрошенным символом: посторонние записи выдачи
				// в справочник не попадают.
				fetched.AddRange(page.List.Where(info => string.Equals(info.Symbol, pair.Symbol, StringComparison.Ordinal)));
			}

			insertedCount += await _store.WriteAsync(categoryGroups.Key, fetched, progressRun, cancellationToken).ConfigureAwait(false);
		}

		return insertedCount;
	}
}
