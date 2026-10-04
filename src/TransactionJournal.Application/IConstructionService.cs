using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application;

/// <summary>
/// Контракт use-case сервиса управления конструкциями для тонких слоёв UI:
/// экран деталей выполняет действия конструкции через этот интерфейс,
/// не завися от конкретного сервиса и хранилища.
/// </summary>
// UI — тонкий слой над готовыми контрактами: мутации конструкции идут
// через доменный use-case сервис, экранные тесты подменяют его заглушкой.
// Traceability: openspec:ui/screens#requirement-construction-actions
public interface IConstructionService
{
	/// <summary>Создаёт конструкцию с именем, необязательным капиталом и статусом «открыта».</summary>
	Task<Construction> CreateAsync(
		string name,
		decimal? allocatedCapitalUsdt,
		string? comment = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Свободно переименовывает конструкцию, не затрагивая прочие данные;
	/// ручное имя фиксируется и больше не перезаписывается автогенерацией сборки.
	/// </summary>
	Task RenameAsync(
		long constructionId,
		string newName,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Меняет выделенный капитал — базу процентов результата; null убирает значение.
	/// </summary>
	Task UpdateAllocatedCapitalAsync(
		long constructionId,
		decimal? allocatedCapitalUsdt,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Задаёт или очищает риск конструкции: значение и единица ввода меняются
	/// только парой — оба заданы или оба отсутствуют.
	/// </summary>
	Task UpdateRiskAsync(
		long constructionId,
		decimal? value,
		TargetUnit? unit,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Задаёт или очищает профит конструкции: значение и единица ввода меняются
	/// только парой — оба заданы или оба отсутствуют.
	/// </summary>
	Task UpdateProfitAsync(
		long constructionId,
		decimal? value,
		TargetUnit? unit,
		CancellationToken cancellationToken = default);

	/// <summary>Свободно меняет ручной статус конструкции, включая архив.</summary>
	Task ChangeStatusAsync(
		long constructionId,
		ConstructionStatus status,
		CancellationToken cancellationToken = default);

	/// <summary>Переводит конструкцию в статус «архив», скрывая её из активных списков.</summary>
	Task ArchiveAsync(long constructionId, CancellationToken cancellationToken = default);

	/// <summary>Удаляет только пустую конструкцию; непустая отказывает с причиной.</summary>
	Task DeleteAsync(long constructionId, CancellationToken cancellationToken = default);

	/// <summary>Возвращает активный список конструкций без архивных.</summary>
	Task<IReadOnlyList<Construction>> ListActiveAsync(CancellationToken cancellationToken = default);

	/// <summary>Возвращает полный список конструкций для аналитики, включая архивные.</summary>
	Task<IReadOnlyList<Construction>> ListAllAsync(CancellationToken cancellationToken = default);
}
