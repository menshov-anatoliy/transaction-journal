using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Components;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Web;

/// <summary>
/// Проверки рендеринга комментариев из Markdown: списки, выделение и код
/// показываются форматированными; одиночный перенос строки отображается
/// разрывом строки, как ждут от поля комментария; сырой HTML экранируется
/// и не попадает в вывод исполняемыми тегами.
/// Traceability: openspec:ui/screens#requirement-comments-inline-editing
/// </summary>
[TestClass]
public class CommentMarkdownTests
{
	[TestMethod]
	[Description("Список, выделение и код рендерятся форматированными элементами")]
	public void TryIfListEmphasisAndCodeRenderFormatted()
	{
		// Arrange: комментарий с разметкой Markdown — список, выделение, код.
		var comment = "- цель: вход половиной\n- **стоп** под минимумом\n`0.1 BTC`";

		// Act
		var html = CommentMarkdown.Render(comment);

		// Assert: разметка превращена в форматированные элементы, а не показана
		// исходным текстом. Требование: список, выделение и фрагмент кода
		// отображаются форматированными.
		// Traceability: openspec:ui/screens#scenario-comment-renders-markdown
		Assert.That(html, Is.Not.Null);
		Assert.That(html!, Does.Contain("<ul>"));
		Assert.That(html, Does.Contain("<li>цель: вход половиной</li>"));
		Assert.That(html, Does.Contain("<strong>стоп</strong>"));
		Assert.That(html, Does.Contain("<code>0.1 BTC</code>"));
	}

	[TestMethod]
	[Description("Одиночный перенос строки отображается разрывом строки")]
	public void TryIfSingleNewlineRendersLineBreak()
	{
		// Arrange: перенос без пустой строки и без разметки Markdown.
		var comment = "вход половиной\nстоп под минимумом";

		// Act
		var html = CommentMarkdown.Render(comment);

		// Assert: мягкий перенос стал жёстким разрывом строки, а не остался
		// исходным переводом строки. Требование: одиночный перенос отображается
		// разрывом строки.
		// Traceability: openspec:ui/screens#scenario-comment-single-newline-breaks-line
		Assert.That(html, Does.Contain("вход половиной<br />"));
		Assert.That(html, Does.Contain("стоп под минимумом</p>"));
	}

	[TestMethod]
	[Description("Сырой HTML экранируется и не попадает в вывод исполняемыми тегами")]
	public void TryIfRawHtmlIsEscapedAndNotExecutable()
	{
		// Arrange: комментарий с попыткой внедрить исполняемую разметку.
		var comment = "<script>alert('x')</script>\n<img src=x onerror=alert('x')>";

		// Act
		var html = CommentMarkdown.Render(comment);

		// Assert: теги не попали в вывод исполняемыми — угловые скобки
		// экранированы сущностями. Требование: сырой HTML отображается как текст
		// и не исполняется браузером.
		// Traceability: openspec:ui/screens#scenario-comment-raw-html-escaped
		Assert.That(html, Does.Contain("&lt;script&gt;"));
		Assert.That(html, Does.Contain("&lt;img"));
		Assert.That(html, Does.Not.Contain("<script"));
		Assert.That(html, Does.Not.Contain("<img"));
	}

	[TestMethod]
	[Description("Опасные схемы ссылок не создают активные ссылки")]
	public void TryIfUnsafeLinkSchemesRenderAsText()
	{
		// Arrange: ссылки пытаются использовать схемы, исполняемые или загружающие данные в браузере.
		var comment = "[скрипт](javascript:alert(document.cookie)) [данные](data:text/html,опасно) [сайт](https://example.com) [раздел](/local)";

		// Act
		var html = CommentMarkdown.Render(comment);

		// Assert: подписи остаются текстом, но опасные URL не попадают в HTML-ссылки.
		// Traceability: openspec:ui/screens#scenario-comment-unsafe-link-schemes-not-rendered
		Assert.That(html, Does.Contain("скрипт"));
		Assert.That(html, Does.Contain("данные"));
		Assert.That(html, Does.Not.Contain("href=\"javascript:"));
		Assert.That(html, Does.Not.Contain("href=\"data:"));
		Assert.That(html, Does.Contain("<a href=\"https://example.com\">сайт</a>"));
		Assert.That(html, Does.Contain("<a href=\"/local\">раздел</a>"));
	}

	[TestMethod]
	[Description("Null и пустой комментарий рендерятся в null")]
	public void TryIfNullOrWhitespaceRendersNull()
	{
		// Отсутствующий комментарий не даёт пустой обёртки — место отображения
		// показывает прочерк.
		Assert.That(CommentMarkdown.Render(null), Is.Null);
		Assert.That(CommentMarkdown.Render(string.Empty), Is.Null);
	}
}
