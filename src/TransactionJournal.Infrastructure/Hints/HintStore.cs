namespace TransactionJournal.Infrastructure.Hints;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Infrastructure.Data;

/// <summary>
/// Адаптер порта хранения подсказок поверх той же SQLite базы журнала:
/// запись подсказки хранится всей историей, терминальные статусы не удаляются
/// и reopen не имеют. Каждый вызов создаёт короткоживущий контекст, поэтому
/// хранилище безопасно в длительных сессиях Blazor Server и фоновых проходах.
// Traceability: openspec:hints/hint-lifecycle#requirement-hint-self-describing-record
/// </summary>
public sealed class HintStore : IHintStore
{
	// Компактный JSON: поля денормализованного снимка читаются адаптером, а не человеком.
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = false,
	};

	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт хранилище подсказок над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public HintStore(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	/// <inheritdoc cref="IHintStore.AddAsync" />
	public async Task<HintRecord> AddAsync(HintRecord hint, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(hint);

		using var db = new JournalDbContext(_options);
		var entity = ToEntity(hint);
		db.Hints.Add(entity);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return ToRecord(entity);
	}

	/// <inheritdoc cref="IHintStore.FindWindowEntryAsync" />
	public async Task<HintRecord?> FindWindowEntryAsync(
		string ruleId,
		HintSubject subject,
		string? periodKey,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
		ArgumentNullException.ThrowIfNull(subject);

		using var db = new JournalDbContext(_options);
		var query = db.Hints
			.AsNoTracking()
			.Where(hint => hint.RuleId == ruleId
				&& hint.SubjectKind == subject.Kind.ToString()
				&& hint.SubjectConstructionId == subject.ConstructionId
				&& hint.WindowPeriodKey == periodKey);

		// Окно представляют сами записи: текущее окно — живой статус
		// new/applied/dismissed, expired его не закрывает.
		// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
		query = query.Where(hint => hint.Status != HintStatus.Expired.ToString());

		var entity = await query
			.OrderByDescending(hint => hint.Id)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
		return entity == null ? null : ToRecord(entity);
	}

	/// <inheritdoc cref="IHintStore.ListLiveAsync" />
	public async Task<IReadOnlyList<HintRecord>> ListLiveAsync(CancellationToken cancellationToken = default)
	{
		using var db = new JournalDbContext(_options);
		var entities = await db.Hints
			.AsNoTracking()
			.Where(hint => hint.Status != HintStatus.Applied.ToString()
				&& hint.Status != HintStatus.Dismissed.ToString()
				&& hint.Status != HintStatus.Expired.ToString())
			.OrderBy(hint => hint.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return entities.Select(ToRecord).ToList();
	}

	/// <inheritdoc cref="IHintStore.ListAllAsync" />
	public async Task<IReadOnlyList<HintRecord>> ListAllAsync(CancellationToken cancellationToken = default)
	{
		using var db = new JournalDbContext(_options);
		var entities = await db.Hints
			.AsNoTracking()
			.OrderBy(hint => hint.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return entities.Select(ToRecord).ToList();
	}

	/// <inheritdoc cref="IHintStore.TryTransitionAsync" />
	public async Task<bool> TryTransitionAsync(
		long id,
		HintStatus targetStatus,
		CancellationToken cancellationToken = default)
	{
		// Перевод допустим только в терминальный статус и только из new:
		// applied и dismissed ставит человек в UI, expired — агент; reopen нет.
		// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
		if (targetStatus == HintStatus.New)
		{
			return false;
		}

		using var db = new JournalDbContext(_options);
		var entity = await db.Hints
			.FirstOrDefaultAsync(hint => hint.Id == id, cancellationToken)
			.ConfigureAwait(false);
		if (entity == null || entity.Status != HintStatus.New.ToString())
		{
			return false;
		}

		entity.Status = targetStatus.ToString();
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return true;
	}

	/// <inheritdoc cref="IHintStore.MarkFirstSeenAsync" />
	public async Task MarkFirstSeenAsync(
		long id,
		DateTimeOffset seenAt,
		CancellationToken cancellationToken = default)
	{
		using var db = new JournalDbContext(_options);
		var entity = await db.Hints
			.FirstOrDefaultAsync(hint => hint.Id == id, cancellationToken)
			.ConfigureAwait(false);

		// firstSeenAt — автопометка первого показа: уже проставленное значение
		// повторными показами не меняется.
		// Traceability: openspec:hints/hint-lifecycle#scenario-hint-first-seen-once
		if (entity == null || entity.FirstSeenAt != null)
		{
			return;
		}

		entity.FirstSeenAt = seenAt;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#region Отображение записи порта на строку хранилища

	private static HintEntity ToEntity(HintRecord hint) => new()
	{
		RuleId = hint.RuleId,
		SubjectKind = hint.Subject.Kind.ToString(),
		SubjectConstructionId = hint.Subject.ConstructionId,
		Character = hint.Character,
		Clarity = hint.Clarity,
		SourcesJson = JsonSerializer.Serialize(hint.Sources, JsonOptions),
		Text = hint.Text,
		FactsJson = JsonSerializer.Serialize(hint.Facts, JsonOptions),
		AsOf = hint.AsOf,
		Status = hint.Status.ToString(),
		FirstSeenAt = hint.FirstSeenAt,
		WindowPeriodKey = hint.WindowPeriodKey,
	};

	private static HintRecord ToRecord(HintEntity entity) => new()
	{
		Id = entity.Id,
		RuleId = entity.RuleId,
		Subject = new HintSubject
		{
			Kind = Enum.Parse<HintSubjectKind>(entity.SubjectKind),
			ConstructionId = entity.SubjectConstructionId,
		},
		Character = entity.Character,
		Clarity = entity.Clarity,
		Sources = DeserializeSources(entity.SourcesJson),
		Text = entity.Text,
		Facts = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.FactsJson, JsonOptions)
			?? new Dictionary<string, string>(),
		AsOf = entity.AsOf,
		Status = Enum.Parse<HintStatus>(entity.Status),
		FirstSeenAt = entity.FirstSeenAt,
		WindowPeriodKey = entity.WindowPeriodKey,
	};

	private static IReadOnlyList<HintSourceTag> DeserializeSources(string json) =>
		JsonSerializer.Deserialize<List<HintSourceTag>>(json, JsonOptions) ?? [];

	#endregion
}
