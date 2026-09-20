using Microsoft.EntityFrameworkCore;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Domain;

namespace TransactionJournal.Analytics;

/// <summary>
/// Свежая марка инструмента со временем её получения: результат запроса провайдера
/// к публичным тикерам. Время получения становится отметкой `marks_as_of` оценки
/// нереализованного PnL и штампом строки кэша.
/// </summary>
public sealed record InstrumentMarkSnapshot(string Symbol, decimal MarkPrice, DateTimeOffset ReceivedAt);

/// <summary>
/// Провайдер марок инструментов: публичный эндпоинт тикеров без аутентификации
/// и кэш последней известной марки в хранилище журнала. Свежая марка запрашивается
/// у биржи по символу и тут же штампуется временем получения; читающие слои берут
/// последнюю известную марку из кэша без сетевого запроса — так ручная пометка
/// закрытия без цены получает дефолт, а оценка нереализованного PnL — марку на
/// момент запроса. Каждый вызов создаёт собственный короткоживущий контекст,
/// поэтому провайдер безопасен в длительных сессиях Blazor Server.
// Traceability: openspec:analytics/performance#requirement-mark-provider
// Traceability: change:add-analytics/design#d2
/// </summary>
public sealed class InstrumentMarkProvider : IInstrumentMarkSource, IFreshInstrumentMarkSource
{
	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly BybitTickersClient _tickersClient;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт провайдер над опциями контекста журнала и клиентом публичных тикеров; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="tickersClient">Клиент публичного эндпоинта тикеров — источник свежих марок.</param>
	/// <param name="timeProvider">Поставщик времени для штамповки момента получения марки; по умолчанию системный.</param>
	/// <exception cref="ArgumentNullException">Опции или клиент тикеров не заданы.</exception>
	public InstrumentMarkProvider(
		DbContextOptions<JournalDbContext> options,
		BybitTickersClient tickersClient,
		TimeProvider? timeProvider = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_tickersClient = tickersClient ?? throw new ArgumentNullException(nameof(tickersClient));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	#region Свежая марка

	/// <summary>
	/// Запрашивает свежую марку инструмента у публичного эндпоинта тикеров и
	/// штампует ею кэш: в хранилище сохраняются и цена, и время её получения,
	/// поэтому строка кэша всегда несёт последнюю известную марку с моментом
	/// её получения. Категория запроса выводится из справочника инструментов
	/// синхронизации; неизвестный справочнику символ не запрашивается вовсе.
	/// </summary>
	/// <param name="symbol">Символ инструмента, например BTCUSDT или BTC-29DEC23-25000-C.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Свежая марка со временем получения; null — инструмент неизвестен справочнику или биржа не отдала марку.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов; решение о деградации остаётся за вызывающим слоем.</exception>
	public async Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		// Категория обязательна для эндпоинта тикеров: берём её из справочника
		// инструментов, который ведёт синхронизация, — по одному запросу на символ.
		// Traceability: change:add-analytics/design#d2
		string? category;
		using (var db = new JournalDbContext(_options))
		{
			category = await db.RawInstruments
				.AsNoTracking()
				.Where(instrument => instrument.Symbol == symbol)
				.Select(instrument => instrument.Category)
				.FirstOrDefaultAsync(cancellationToken)
				.ConfigureAwait(false);
		}

		if (category == null)
		{
			// Инструмент вне справочника — запрос тикеров не построить; вызывающий
			// слой получает null и сам решает, как деградировать.
			return null;
		}

		var tickers = await _tickersClient.GetTickersAsync(new BybitTickerQuery
		{
			Category = category,
			Symbol = symbol,
		}, cancellationToken).ConfigureAwait(false);
		var markPrice = tickers
			.FirstOrDefault(ticker => string.Equals(ticker.Symbol, symbol, StringComparison.Ordinal))?.MarkPrice;
		if (markPrice == null)
		{
			// Биржа не отдала марку инструмента — кэш не трогаем, последняя известная
			// марка остаётся прежней.
			return null;
		}

		// Кэш штампуется временем: вместе с ценой сохраняется момент её получения.
		// Traceability: openspec:analytics/performance#scenario-mark-cache-timestamped
		var receivedAt = _timeProvider.GetUtcNow();
		using (var db = new JournalDbContext(_options))
		{
			var cached = await db.InstrumentMarks
				.FirstOrDefaultAsync(mark => mark.Symbol == symbol, cancellationToken)
				.ConfigureAwait(false);
			if (cached == null)
			{
				db.InstrumentMarks.Add(new InstrumentMark
				{
					Symbol = symbol,
					MarkPrice = markPrice.Value,
					ReceivedAt = receivedAt,
				});
			}
			else
			{
				cached.MarkPrice = markPrice.Value;
				cached.ReceivedAt = receivedAt;
			}

			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return new InstrumentMarkSnapshot(symbol, markPrice.Value, receivedAt);
	}

	#endregion

	#region Последняя известная марка

	/// <summary>
	/// Возвращает последнюю известную марку инструмента из кэша провайдера —
	/// без сетевого запроса. Служит дефолтом цены ручной пометки закрытия,
	/// когда пользователь не задал цену: read-модель позиций подставляет марку
	/// при чтении. Марка неизвестна, пока провайдер не получил ни одной свежей
	/// марки инструмента.
	/// </summary>
	/// <param name="symbol">Инструмент, марка которого нужна.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Последняя известная марка или null, если марка ещё не известна.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	public async Task<decimal?> GetLastMarkAsync(string symbol, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		// Дефолт ручной пометки читает кэш: пользователь вправе задать цену явно,
		// а возраст последней известной марки показывается по её штампу времени.
		// Traceability: openspec:analytics/performance#scenario-last-known-mark-serves-manual-default
		using var db = new JournalDbContext(_options);
		var markPrice = await db.InstrumentMarks
			.AsNoTracking()
			.Where(mark => mark.Symbol == symbol)
			.Select(mark => (decimal?)mark.MarkPrice)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
		return markPrice;
	}

	#endregion
}
