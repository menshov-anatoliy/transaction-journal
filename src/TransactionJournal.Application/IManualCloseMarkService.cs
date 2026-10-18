using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application;

/// <summary>
/// Контракт use-case сервиса ручных пометок закрытия для тонких слоёв UI:
/// экран деталей ставит пометку из строки открытой позиции и правит либо
/// удаляет её из таблицы закрывающих записей, не завися от конкретного
/// сервиса и хранилища.
/// </summary>
// Ручная пометка закрытия ставится из строки позиции и сопровождается
// предупреждением об избыточной записи — мутации идут через доменный контракт.
// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
public interface IManualCloseMarkService
{
	/// <summary>Добавляет ручную пометку закрытия позиции конструкции; цена null подставляется последней маркой при чтении.</summary>
	/// <param name="quantity">Количество закрываемой позиции; null — весь остаток на момент пометки.</param>
	Task<ManualCloseMark> AddAsync(
		long constructionId,
		string symbol,
		DateTimeOffset markedAt,
		decimal? price = null,
		decimal? quantity = null,
		CancellationToken cancellationToken = default);

	/// <summary>Свободно правит инструмент, время, цену и количество ручной пометки закрытия.</summary>
	/// <param name="quantity">Новое количество; null — закрыть весь остаток на момент пометки.</param>
	Task EditAsync(
		long markId,
		string symbol,
		DateTimeOffset markedAt,
		decimal? price,
		decimal? quantity,
		CancellationToken cancellationToken = default);

	/// <summary>Удаляет ручную пометку закрытия — позиция возвращается в открытое состояние ближайшим чтением.</summary>
	Task DeleteAsync(long markId, CancellationToken cancellationToken = default);

	/// <summary>Возвращает ручные пометки конструкции в хронологическом порядке.</summary>
	Task<IReadOnlyList<ManualCloseMark>> ListAsync(
		long constructionId,
		CancellationToken cancellationToken = default);
}
