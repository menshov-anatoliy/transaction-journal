using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application;
using TransactionJournal.Domain;

namespace TransactionJournal.Infrastructure.UseCases;

/// <summary>
/// Use-case сервис оспаривания распознанных синтетических связок: оспоренная
/// связка исключается из логического слоя до явного возврата, запись оспаривания
/// переживает инкрементную сборку и стирается полным пересбором вместе с
/// содержимым конструкций. Оспаривание не меняет инструментальных данных —
/// сервис пишет только ручные записи логического слоя. Каждый вызов создаёт
/// собственный короткоживущий контекст, поэтому сервис безопасен в длительных
/// сессиях Blazor Server.
// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-dispute
/// Traceability: change:synthetic-close-logical-layer/design#d3
/// </summary>
public sealed class SyntheticCloseDisputeService : ISyntheticCloseDisputeService
{
	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт сервис над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="timeProvider">Поставщик времени для момента оспаривания; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public SyntheticCloseDisputeService(
		DbContextOptions<JournalDbContext> options,
		TimeProvider? timeProvider = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	#region Оспаривание

	/// <summary>
	/// Оспаривает распознанную связку конструкции: запись по паре execId делается
	/// спорной, и связка исключается из логического слоя до явного возврата.
	/// Повторное оспаривание той же связки безвредно — спор связки один, запись
	/// не дублируется уникальным индексом. Связка с ручными сделками оспариванию
	/// не подлежит: спор предусмотрен только распознанным связкам, ручная
	/// удаляется свободно.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой оспаривается связка.</param>
	/// <param name="linearExecId">Биржевой идентификатор линейного исполнения связки.</param>
	/// <param name="counterOptionExecId">Биржевой идентификатор встречного опционного исполнения связки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Идентификатор исполнения не задан или состоит из пробелов.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	/// <exception cref="SyntheticCloseDisputeNotApplicableException">Пара адресует ручные сделки.</exception>
	public async Task DisputeAsync(
		long constructionId,
		string linearExecId,
		string counterOptionExecId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(linearExecId);
		ArgumentException.ThrowIfNullOrWhiteSpace(counterOptionExecId);

		// Ручные сделки адресуются единым ключом «manual:{id}»: спор по такой паре
		// не сохраняется — вместо no-op-записи клиенту отказывают с объяснением.
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		if (SyntheticCloseManualLinkMaterial.TryParseManualKey(linearExecId, out _)
			|| SyntheticCloseManualLinkMaterial.TryParseManualKey(counterOptionExecId, out _))
		{
			throw new SyntheticCloseDisputeNotApplicableException(linearExecId, counterOptionExecId);
		}

		// Оспаривание идемпотентно: существующий спор той же связки остаётся
		// прежней записью, повторный вызов не меняет момент оспаривания.
		// Traceability: change:synthetic-close-logical-layer/design#d3
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var existing = await FindDisputeAsync(db, constructionId, linearExecId, counterOptionExecId, cancellationToken)
			.ConfigureAwait(false);
		if (existing is not null)
		{
			return;
		}

		db.SyntheticCloseDisputes.Add(new SyntheticCloseDispute
		{
			ConstructionId = constructionId,
			LinearExecId = linearExecId,
			CounterOptionExecId = counterOptionExecId,
			DisputedAt = _timeProvider.GetUtcNow(),
		});
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Возврат

	/// <summary>
	/// Возвращает оспоренную связку: запись оспаривания удаляется, и связка
	/// распознаётся снова автоматически ближайшим чтением — распознавание
	/// остаётся чистой функцией записей.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой возвращается связка.</param>
	/// <param name="linearExecId">Биржевой идентификатор линейного исполнения связки.</param>
	/// <param name="counterOptionExecId">Биржевой идентификатор встречного опционного исполнения связки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Идентификатор исполнения не задан или состоит из пробелов.</exception>
	/// <exception cref="SyntheticCloseDisputeNotFoundException">Связка не была оспорена.</exception>
	public async Task RevertAsync(
		long constructionId,
		string linearExecId,
		string counterOptionExecId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(linearExecId);
		ArgumentException.ThrowIfNullOrWhiteSpace(counterOptionExecId);

		// Возврат свободен: удаление записи возвращает связку распознаванию без
		// синхронных правок производного слоя.
		// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
		using var db = CreateContext();
		var dispute = await FindDisputeAsync(db, constructionId, linearExecId, counterOptionExecId, cancellationToken)
			.ConfigureAwait(false)
			?? throw new SyntheticCloseDisputeNotFoundException(constructionId, linearExecId, counterOptionExecId);
		db.SyntheticCloseDisputes.Remove(dispute);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

	/// <summary>Проверяет существование конструкции до изменения споров.</summary>
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

	/// <summary>Загружает спор по ключу «конструкция + пара execId».</summary>
	private static async Task<SyntheticCloseDispute?> FindDisputeAsync(
		JournalDbContext db,
		long constructionId,
		string linearExecId,
		string counterOptionExecId,
		CancellationToken cancellationToken)
	{
		return await db.SyntheticCloseDisputes
			.FirstOrDefaultAsync(dispute =>
				dispute.ConstructionId == constructionId
				&& dispute.LinearExecId == linearExecId
				&& dispute.CounterOptionExecId == counterOptionExecId, cancellationToken)
			.ConfigureAwait(false);
	}

	#endregion
}
