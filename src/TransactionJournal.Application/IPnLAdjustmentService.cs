using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application;

/// <summary>
/// Контракт use-case сервиса внешних корректировок PnL для тонких слоёв UI:
/// экран деталей добавляет корректировку формой и правит либо удаляет её
/// строкой таблицы, не завися от конкретного сервиса и хранилища.
/// </summary>
// Корректировки PnL живут в деталях конструкции — мутации идут через
// доменный контракт, отдельных экранов не появляется.
// Traceability: openspec:ui/screens#requirement-adjustments-in-detail
public interface IPnLAdjustmentService
{
	/// <summary>Добавляет внешнюю корректировку PnL к конструкции со знаковой суммой в USDT и комментарием.</summary>
	Task<PnLAdjustment> AddAsync(
		long constructionId,
		DateTimeOffset date,
		PnLAdjustmentSource source,
		decimal amountUsdt,
		string? comment = null,
		CancellationToken cancellationToken = default);

	/// <summary>Свободно правит все атрибуты внешней корректировки PnL: дату, источник, сумму и комментарий.</summary>
	Task EditAsync(
		long adjustmentId,
		DateTimeOffset date,
		PnLAdjustmentSource source,
		decimal amountUsdt,
		string? comment,
		CancellationToken cancellationToken = default);

	/// <summary>Удаляет внешнюю корректировку PnL — результат пересчитывается ближайшим чтением.</summary>
	Task DeleteAsync(long adjustmentId, CancellationToken cancellationToken = default);

	/// <summary>Возвращает внешние корректировки PnL конструкции в хронологическом порядке.</summary>
	Task<IReadOnlyList<PnLAdjustment>> ListAsync(
		long constructionId,
		CancellationToken cancellationToken = default);
}
