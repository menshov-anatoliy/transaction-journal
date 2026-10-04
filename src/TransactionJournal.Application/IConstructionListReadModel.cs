using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;

namespace TransactionJournal.Application;

/// <summary>
/// Read-модель экрана «Конструкции»: соединяет метрики аналитики журнала
/// с именами и ручными статусами конструкций и скрывает архивные из списка.
/// Каркас метрик принадлежит аналитике — модель тонкая: читает готовые метрики,
/// не считает ничего сама и ничего не мутирует.
/// </summary>
// Источник смысла: сводка журнала и таблица конструкций определены требованием экрана.
// Traceability: openspec:ui/screens#requirement-construction-list-screen
public interface IConstructionListReadModel
{
	/// <summary>
	/// Читает данные экрана «Конструкции» из текущего состояния журнала:
	/// сводку журнала и строки таблицы конструкций без архивных.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="Materialization.TradeMaterializationException">Сырая запись исполнения повреждена или конфликтует по execId.</exception>
	/// <exception cref="Materialization.ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует по ключу.</exception>
	/// <exception cref="Materialization.InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	Task<ConstructionListData> ReadAsync(CancellationToken cancellationToken = default);
}
