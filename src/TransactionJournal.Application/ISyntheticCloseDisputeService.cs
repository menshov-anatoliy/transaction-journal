using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application;

/// <summary>
/// Контракт use-case сервиса оспаривания распознанных синтетических связок
/// для тонких слоёв UI: строка связки оспаривается из деталей конструкции и
/// возвращается явным действием, слой UI не зависит от конкретного сервиса
/// и хранилища.
/// </summary>
// Спор — ручные данные логического слоя: инкрементная сборка их переживает,
// полный пересбор стирает вместе с содержимым конструкций.
// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-dispute
public interface ISyntheticCloseDisputeService
{
	/// <summary>Оспаривает распознанную связку конструкции: связка исключается из логического слоя до явного возврата.</summary>
	Task DisputeAsync(
		long constructionId,
		string linearExecId,
		string counterOptionExecId,
		CancellationToken cancellationToken = default);

	/// <summary>Возвращает оспоренную связку: запись оспаривания удаляется, распознавание снова её находит.</summary>
	Task RevertAsync(
		long constructionId,
		string linearExecId,
		string counterOptionExecId,
		CancellationToken cancellationToken = default);

	/// <summary>Возвращает записи оспаривания конструкции по паре execId связок.</summary>
	Task<IReadOnlyList<SyntheticCloseDispute>> ListAsync(
		long constructionId,
		CancellationToken cancellationToken = default);
}
