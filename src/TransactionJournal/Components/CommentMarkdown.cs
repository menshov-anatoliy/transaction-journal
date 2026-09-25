using Markdig;

namespace TransactionJournal.Components;

/// <summary>
/// Отображение комментариев журнала: текст комментария рендерится из Markdown
/// в HTML. Пайплайн усечён: одиночный перенос строки даёт разрыв строки, как
/// ждут от поля комментария; сырой HTML в комментарии парсится как текст и
/// экранируется на выходе — браузер его не исполняет. Хранение не меняется:
/// комментарий остаётся свободным текстом с внутренними переносами.
/// Traceability: openspec:ui/screens#requirement-comments-inline-editing
/// </summary>
public static class CommentMarkdown
{
	/// <summary>Усечённый пайплайн CommonMark для комментариев: без HTML,
	/// с автоссылками и мягким переносом как жёстким разрывом строки.</summary>
	// Сырой HTML выключен целиком (DisableHtml): разметка комментария
	// отображается как текст и не попадает в вывод исполняемыми тегами.
	// Traceability: openspec:ui/screens#scenario-comment-raw-html-escaped
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
		.UseSoftlineBreakAsHardlineBreak()
		.UseAutoLinks()
		.DisableHtml()
		.Build();

	/// <summary>HTML комментария, отрендеренный из Markdown; null/пустой вход — null.</summary>
	// Одиночный перенос строки без пустой строки отображается разрывом
	// строки, а не исходным символом перевода строки.
	// Traceability: openspec:ui/screens#scenario-comment-single-newline-breaks-line
	public static string? Render(string? comment)
	{
		if (string.IsNullOrEmpty(comment))
		{
			return null;
		}

		return Markdown.ToHtml(comment, Pipeline);
	}
}
