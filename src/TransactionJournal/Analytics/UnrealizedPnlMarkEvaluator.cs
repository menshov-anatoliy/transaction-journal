using System.Net.Http;
using TransactionJournal.Bybit;

namespace TransactionJournal.Analytics;

/// <summary>
/// Оценка нереализованного PnL открытых остатков по маркам публичных тикеров
/// на момент запроса. Открытому остатку подставляется свежая марка инструмента
/// и нереализованная оценка — разница марки и средней цены открытого остатка,
/// умноженная на знаковый остаток; вместе с оценкой возвращается отметка
/// времени марок. Недоступность тикеров — деградация, а не ошибка: сбой обнуляет
/// только нереализованную часть и отметку времени, реализованные метрики
/// позиций возвращаются нетронутыми.
// Traceability: openspec:analytics/performance#requirement-unrealized-pnl-current-marks
// Traceability: change:add-analytics/design#d3
/// </summary>
public sealed class UnrealizedPnlMarkEvaluator
{
	private readonly IFreshInstrumentMarkSource _markSource;

	/// <summary>Создаёт оценщик над источником свежих марок; сам оценщик чистый и состояния между вызовами не хранит.</summary>
	/// <param name="markSource">Источник свежих марок — провайдер марок публичных тикеров.</param>
	/// <exception cref="ArgumentNullException">Источник марок не задан.</exception>
	public UnrealizedPnlMarkEvaluator(IFreshInstrumentMarkSource markSource)
	{
		_markSource = markSource ?? throw new ArgumentNullException(nameof(markSource));
	}

	/// <summary>
	/// Оценивает нереализованный PnL открытых остатков метрик позиций: по каждому
	/// инструменту открытого остатка запрашивается свежая марка на момент вызова,
	/// закрытые позиции марок не требуют вовсе. Успешная оценка возвращает метрики
	/// с заполненными маркой и нереализованным PnL и отметку времени марок — момент
	/// получения старейшей из использованных марок. Сбой провайдера хотя бы у одного
	/// инструмента деградирует: позиции без марки оставляют нереализованную часть
	/// null, отметка времени — null с признаком сбоя, реализованные метрики
	/// возвращаются как есть.
	/// </summary>
	/// <param name="positions">Метрики позиций после калькулятора метрик позиции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Оценка: метрики позиций с нереализованной частью, отметка времени марок и признак сбоя.</returns>
	/// <exception cref="ArgumentNullException">Метрики позиций не заданы.</exception>
	public async Task<UnrealizedPnlEvaluation> EvaluateAsync(
		IEnumerable<PositionMetrics> positions,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(positions);

		var positionList = positions.ToList();

		// Марки нужны только открытым остаткам: закрытая позиция не имеет
		// нереализованной части, поэтому её инструмент не запрашивается вовсе.
		// Traceability: openspec:analytics/performance#scenario-closed-position-has-no-unrealized
		var openSymbols = positionList
			.Where(position => position.Residual != 0m)
			.Select(position => position.Symbol)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		// Свежая марка запрашивается один раз на инструмент: марка позиции и оценка
		// нереализованной части берутся тем же запросом, без повторных обращений
		// к публичным тикерам.
		// Traceability: change:add-analytics/design#d1
		var marks = new Dictionary<string, InstrumentMarkSnapshot>(StringComparer.Ordinal);
		var hasMarkFailure = false;
		foreach (var symbol in openSymbols)
		{
			InstrumentMarkSnapshot? mark;
			try
			{
				mark = await _markSource.GetFreshMarkAsync(symbol, cancellationToken).ConfigureAwait(false);
			}
			catch (BybitApiException)
			{
				// Биржа ответила ошибкой после всех повторов — тикеры недоступны:
				// это деградация оценки, а не сбой чтения метрик.
				// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
				hasMarkFailure = true;
				continue;
			}
			catch (HttpRequestException)
			{
				// Сеть до публичных тикеров не дошла — та же деградация без подъёма
				// ошибки наружу.
				hasMarkFailure = true;
				continue;
			}
			catch (TaskCanceledException exception) when (exception.InnerException is TimeoutException)
			{
				// Истёк таймаут запроса тикеров — тикеры недоступны в момент запроса;
				// отмена по токену под этот фильтр не попадает и поднимается наружу.
				hasMarkFailure = true;
				continue;
			}

			if (mark == null)
			{
				// Инструмент неизвестен справочнику либо биржа не отдала марку —
				// оценке этого остатка не из чего построиться.
				hasMarkFailure = true;
				continue;
			}

			marks.Add(symbol, mark);
		}

		// Отметка времени марок — момент получения марок оценки; честной границей
		// служит старейшая из полученных марок. Оценка неделима: сбой хотя бы одной
		// марки обнуляет отметку времени вместе с нереализованной частью.
		// Traceability: openspec:analytics/performance#scenario-open-residual-valued-at-request
		DateTimeOffset? marksAsOf = hasMarkFailure || marks.Count == 0
			? null
			: marks.Values.Min(mark => mark.ReceivedAt);

		return new UnrealizedPnlEvaluation
		{
			Positions = positionList
				.Select(position => EvaluatePosition(position, marks))
				.ToList(),
			MarksAsOf = marksAsOf,
			HasMarkFailure = hasMarkFailure,
		};
	}

	#region Помощники

	/// <summary>
	/// Подставляет позиции марку и нереализованную оценку: разница марки и средней
	/// цены открытого остатка, умноженная на знаковый остаток, — направление
	/// остатка задаёт знак результата и для длинных, и для коротких позиций.
	/// Позиции без марки возвращаются как есть: их нереализованная часть остаётся
	/// null, остальные метрики не меняются.
	/// </summary>
	private static PositionMetrics EvaluatePosition(PositionMetrics position, Dictionary<string, InstrumentMarkSnapshot> marks)
	{
		// Закрытая позиция оценку не получает: её нереализованная часть уже ноль,
		// марка и средняя цена закрытой позиции не вычисляются.
		if (position.Residual == 0m)
		{
			return position;
		}

		if (marks.TryGetValue(position.Symbol, out var mark) == false || position.AverageOpenPrice == null)
		{
			return position;
		}

		return position with
		{
			MarkPrice = mark.MarkPrice,
			UnrealizedPnL = (mark.MarkPrice - position.AverageOpenPrice.Value) * position.Residual,
		};
	}

	#endregion
}
