using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;

namespace TransactionJournal.Domain;

/// <summary>
/// Use-case сервис комментариев трёх уровней: на сделке (по ключу execId),
/// на позиции (по ключу «конструкция × инструмент» — позиция производная
/// и собственной строки не имеет, поэтому комментарий хранится по стабильному
/// ключу и переживает закрытие, переоткрытие и пересчёты остатка) и на самой
/// конструкции. Комментарии свободны и не влияют на позиции, результаты
/// и статусы. Каждый вызов создаёт собственный короткоживущий контекст,
/// поэтому сервис безопасен в длительных сессиях Blazor Server.
// Traceability: openspec:domain/constructions#requirement-entity-comments
// Traceability: change:add-core-domain/design#d2
/// </summary>
public sealed class CommentService
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт сервис над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public CommentService(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	#region Комментарии сделок

	/// <summary>
	/// Задаёт или снимает комментарий сделки по ключу execId. Комментарий хранится
	/// в пользовательских данных сделки и переживает привязки, возвраты
	/// во «Входящие» и пересчёты; null или пустой текст снимают комментарий,
	/// не трогая привязку.
	/// </summary>
	/// <param name="execId">Биржевой идентификатор исполнения сделки.</param>
	/// <param name="comment">Текст комментария; null или пробелы — снять комментарий.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Идентификатор исполнения не задан или состоит из пробелов.</exception>
	/// <exception cref="TradeNotFoundException">Сделки с таким execId нет в журнале.</exception>
	public async Task SetTradeCommentAsync(
		string execId,
		string? comment,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(execId);

		// Комментарий живёт в строке пользовательских данных сделки по execId:
		// строка создаётся при первом комментарии и не удаляется снятием —
		// привязка и история перепривязок не затрагиваются.
		// Traceability: openspec:domain/constructions#requirement-entity-comments
		using var db = CreateContext();
		await EnsureTradeExistsAsync(db, execId, cancellationToken).ConfigureAwait(false);
		var userdata = await db.TradeUserdata
			.FirstOrDefaultAsync(candidate => candidate.ExecId == execId, cancellationToken)
			.ConfigureAwait(false);
		var normalized = NormalizeText(comment);
		if (userdata == null)
		{
			if (normalized != null)
			{
				db.TradeUserdata.Add(new TradeUserdata { ExecId = execId, Comment = normalized });
			}
		}
		else
		{
			userdata.Comment = normalized;
		}

		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Возвращает комментарий сделки по ключу execId; null — комментария нет.</summary>
	/// <param name="execId">Биржевой идентификатор исполнения сделки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Идентификатор исполнения не задан или состоит из пробелов.</exception>
	/// <exception cref="TradeNotFoundException">Сделки с таким execId нет в журнале.</exception>
	public async Task<string?> GetTradeCommentAsync(string execId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(execId);

		using var db = CreateContext();
		await EnsureTradeExistsAsync(db, execId, cancellationToken).ConfigureAwait(false);
		return await db.TradeUserdata
			.Where(userdata => userdata.ExecId == execId)
			.Select(userdata => userdata.Comment)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	#endregion

	#region Комментарии позиций

	/// <summary>
	/// Задаёт или снимает комментарий позиции по ключу «конструкция × инструмент».
	/// Позиция — производная, собственной строки не имеет, поэтому комментарий
	/// хранится по стабильному ключу и переживает закрытие, переоткрытие
	/// и пересчёты остатка. null или пустой текст удаляют строку комментария.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой оставлен комментарий.</param>
	/// <param name="symbol">Инструмент позиции.</param>
	/// <param name="text">Текст комментария; null или пробелы — снять комментарий.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task SetPositionCommentAsync(
		long constructionId,
		string symbol,
		string? text,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		// Комментарий позиции привязан к ключу, а не к состоянию позиции:
		// остаток может стать нулевым или вырасти заново — комментарий останется.
		// Traceability: openspec:domain/constructions#scenario-position-comment-survives-recompute
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var existing = await db.PositionComments
			.FirstOrDefaultAsync(comment => comment.ConstructionId == constructionId && comment.Symbol == symbol)
			.ConfigureAwait(false);
		var normalized = NormalizeText(text);
		if (normalized == null)
		{
			if (existing != null)
			{
				db.PositionComments.Remove(existing);
			}
		}
		else if (existing != null)
		{
			existing.Text = normalized;
		}
		else
		{
			db.PositionComments.Add(new PositionComment
			{
				ConstructionId = constructionId,
				Symbol = symbol,
				Text = normalized,
			});
		}

		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Возвращает комментарий позиции по ключу «конструкция × инструмент»; null — комментария нет.</summary>
	/// <param name="constructionId">Конструкция, внутри которой оставлен комментарий.</param>
	/// <param name="symbol">Инструмент позиции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<string?> GetPositionCommentAsync(
		long constructionId,
		string symbol,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		return await db.PositionComments
			.Where(comment => comment.ConstructionId == constructionId && comment.Symbol == symbol)
			.Select(comment => comment.Text)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	#endregion

	#region Комментарии конструкций

	/// <summary>
	/// Задаёт или снимает комментарий конструкции. Комментарий хранится
	/// в атрибутах конструкции; null или пустой текст снимают его, не затрагивая
	/// имя, статус, капитал и производные величины.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="comment">Текст комментария; null или пробелы — снять комментарий.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task SetConstructionCommentAsync(
		long constructionId,
		string? comment,
		CancellationToken cancellationToken = default)
	{
		// Комментарий конструкции — свободный атрибут: меняется только столбец
		// комментария, прочие данные и производные величины не затрагиваются.
		// Traceability: openspec:domain/constructions#scenario-comments-inert
		using var db = CreateContext();
		var construction = await db.Constructions
			.FirstOrDefaultAsync(candidate => candidate.Id == constructionId, cancellationToken)
			.ConfigureAwait(false);
		if (construction == null)
		{
			throw new ConstructionNotFoundException(constructionId);
		}

		construction.Comment = NormalizeText(comment);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Возвращает комментарий конструкции; null — комментария нет.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<string?> GetConstructionCommentAsync(
		long constructionId,
		CancellationToken cancellationToken = default)
	{
		using var db = CreateContext();
		var comment = await db.Constructions
			.Where(construction => construction.Id == constructionId)
			.Select(construction => construction.Comment)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);

		// FirstOrDefault строки селекта не отличает «нет конструкции» от «конструкция
		// без комментария» — существование проверяется отдельно.
		if (await db.Constructions.AnyAsync(construction => construction.Id == constructionId, cancellationToken).ConfigureAwait(false) == false)
		{
			throw new ConstructionNotFoundException(constructionId);
		}

		return comment;
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

	/// <summary>Приводит текст комментария к хранимой форме: null или пробелы означают отсутствие комментария.</summary>
	private static string? NormalizeText(string? text) =>
		string.IsNullOrWhiteSpace(text) ? null : text.Trim();

	/// <summary>Проверяет существование сделки в сыром хранилище журнала.</summary>
	/// <exception cref="TradeNotFoundException">Сделка не найдена.</exception>
	private static async Task EnsureTradeExistsAsync(
		JournalDbContext db,
		string execId,
		CancellationToken cancellationToken)
	{
		var exists = await db.RawExecutions
			.AnyAsync(execution => execution.ExecId == execId, cancellationToken)
			.ConfigureAwait(false);
		if (exists == false)
		{
			throw new TradeNotFoundException(execId);
		}
	}

	/// <summary>Проверяет существование конструкции до изменения комментариев.</summary>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	private static async Task EnsureConstructionExistsAsync(
		JournalDbContext db,
		long constructionId,
		CancellationToken cancellationToken)
	{
		var exists = await db.Constructions
			.AnyAsync(construction => construction.Id == constructionId, cancellationToken)
			.ConfigureAwait(false);
		if (exists == false)
		{
			throw new ConstructionNotFoundException(constructionId);
		}
	}

	#endregion
}
