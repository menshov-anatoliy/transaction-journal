using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Domain.Sync;

namespace TransactionJournal.Application;

/// <summary>
/// Read-модель экрана деталей конструкции: соединяет метрики аналитики, поток
/// закрывающих записей read-модели позиций и пользовательские записи хранилища
/// в один снимок экрана. Модель тонкая: читает готовые проекции, не считает
/// метрики сама и ничего не мутирует — изменение данных выполняют сервисы домена,
/// экран перечитывает снимок целиком.
/// </summary>
// Источник смысла: сводка и таблицы записей деталей определены требованием экрана.
// Traceability: openspec:ui/screens#requirement-construction-detail-screen
public interface IConstructionDetailReadModel
{
	/// <summary>
	/// Читает данные экрана деталей конструкции из текущего состояния журнала:
	/// заголовок с комментарием, метрики с периодом и таблицы записей.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	/// <exception cref="Materialization.TradeMaterializationException">Сырая запись исполнения повреждена или конфликтует по execId.</exception>
	/// <exception cref="Materialization.ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует по ключу.</exception>
	/// <exception cref="Materialization.InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	Task<ConstructionDetailData> ReadAsync(long constructionId, CancellationToken cancellationToken = default);
}
