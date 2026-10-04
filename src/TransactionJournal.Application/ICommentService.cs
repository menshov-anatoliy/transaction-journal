
namespace TransactionJournal.Application;

/// <summary>
/// Контракт use-case сервиса комментариев трёх уровней для тонких слоёв UI:
/// экран деталей правит комментарии сделки, позиции и конструкции по месту
/// их отображения, не завися от конкретного сервиса и хранилища.
/// </summary>
// Inline-правка комментариев на месте своего уровня — таблицы сделок и позиций
// и шапка деталей — выполняет сохранение через доменный use-case сервис.
// Traceability: openspec:ui/screens#requirement-comments-inline-editing
public interface ICommentService
{
	/// <summary>Задаёт или снимает комментарий сделки по ключу execId; null или пробелы снимают комментарий.</summary>
	Task SetTradeCommentAsync(string execId, string? comment, CancellationToken cancellationToken = default);

	/// <summary>Задаёт или снимает комментарий позиции по ключу «конструкция × инструмент».</summary>
	Task SetPositionCommentAsync(long constructionId, string symbol, string? text, CancellationToken cancellationToken = default);

	/// <summary>Задаёт или снимает комментарий конструкции; null или пробелы снимают комментарий.</summary>
	Task SetConstructionCommentAsync(long constructionId, string? comment, CancellationToken cancellationToken = default);
}
