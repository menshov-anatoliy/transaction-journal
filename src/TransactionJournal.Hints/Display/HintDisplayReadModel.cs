namespace TransactionJournal.Hints.Display;

using TransactionJournal.Hints.Ports;

/// <summary>
/// Реализация read-модели отображения подсказок над хранилищем подсказок:
/// группировка и сортировка вычисляются при чтении из субъекта и характера
/// записи — хранилище полей группировки не держит. Живые записи — статус
/// new; applied, dismissed и expired — терминальная история.
// Traceability: openspec:ui/screens#requirement-ui-hint-section-groups
// Traceability: openspec:ui/screens#requirement-ui-hint-log
/// </summary>
public sealed class HintDisplayReadModel : IHintDisplayReadModel
{
	private readonly IHintStore _hintStore;

	/// <summary>Создаёт read-модель над портом хранения подсказок.</summary>
	/// <param name="hintStore">Порт хранения подсказок.</param>
	/// <exception cref="ArgumentNullException">Порт не задан.</exception>
	public HintDisplayReadModel(IHintStore hintStore)
	{
		_hintStore = hintStore ?? throw new ArgumentNullException(nameof(hintStore));
	}

	/// <inheritdoc cref="IHintDisplayReadModel.ReadConstructionPanelAsync" />
	public async Task<HintPanelData> ReadConstructionPanelAsync(
		long constructionId,
		CancellationToken cancellationToken = default)
	{
		return BuildPanel(HintSubject.ForConstruction(constructionId), await _hintStore.ListAllAsync(cancellationToken).ConfigureAwait(false));
	}

	/// <inheritdoc cref="IHintDisplayReadModel.ReadJournalPanelAsync" />
	public async Task<HintPanelData> ReadJournalPanelAsync(CancellationToken cancellationToken = default)
	{
		return BuildPanel(HintSubject.ForJournal(), await _hintStore.ListAllAsync(cancellationToken).ConfigureAwait(false));
	}

	/// <inheritdoc cref="IHintDisplayReadModel.ReadLiveCountsByConstructionAsync" />
	public async Task<IReadOnlyDictionary<long, int>> ReadLiveCountsByConstructionAsync(CancellationToken cancellationToken = default)
	{
		var liveRecords = await _hintStore.ListLiveAsync(cancellationToken).ConfigureAwait(false);
		return liveRecords
			.Where(record => record.Subject.Kind == HintSubjectKind.Construction)
			.GroupBy(record => record.Subject.ConstructionId!.Value)
			.ToDictionary(group => group.Key, group => group.Count());
	}

	/// <inheritdoc cref="IHintDisplayReadModel.ReadLogAsync" />
	public async Task<IReadOnlyList<HintRecord>> ReadLogAsync(
		HintLogFilter? filter = null,
		CancellationToken cancellationToken = default)
	{
		var effectiveFilter = filter ?? HintLogFilter.All;
		var groupCharacters = effectiveFilter.GroupCharacters;
		return (await _hintStore.ListAllAsync(cancellationToken).ConfigureAwait(false))
			.Where(record => effectiveFilter.Status is null || record.Status == effectiveFilter.Status)
			.Where(record => effectiveFilter.Character is null || record.Character == effectiveFilter.Character)
			.Where(record => groupCharacters is null || groupCharacters.Contains(record.Character, StringComparer.Ordinal))
			.OrderByDescending(record => record.AsOf)
			.ThenByDescending(record => record.Id)
			.ToList();
	}

	/// <inheritdoc cref="IHintDisplayReadModel.ApplyAsync" />
	// Переход в applied выполняет только человек из UI; агент этот статус не ставит.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	public Task<bool> ApplyAsync(long hintId, CancellationToken cancellationToken = default) =>
		_hintStore.TryTransitionAsync(hintId, HintStatus.Applied, cancellationToken);

	/// <inheritdoc cref="IHintDisplayReadModel.DismissAsync" />
	// Переход в dismissed выполняет только человек из UI; повтор в окне подавлен.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	public Task<bool> DismissAsync(long hintId, CancellationToken cancellationToken = default) =>
		_hintStore.TryTransitionAsync(hintId, HintStatus.Dismissed, cancellationToken);

	/// <inheritdoc cref="IHintDisplayReadModel.MarkSeenAsync" />
	public Task MarkSeenAsync(long hintId, DateTimeOffset seenAt, CancellationToken cancellationToken = default) =>
		_hintStore.MarkFirstSeenAsync(hintId, seenAt, cancellationToken);

	/// <summary>
	/// Собирает панель субъекта: живые new-записи группируются по справочнику
	/// v1 без пустых групп, терминальные уходят в историю; сортировка — по
	/// времени генерации, свежие сверху.
	/// </summary>
	private static HintPanelData BuildPanel(HintSubject subject, IReadOnlyList<HintRecord> allRecords)
	{
		var subjectRecords = allRecords
			.Where(record => record.Subject == subject)
			.OrderByDescending(record => record.AsOf)
			.ThenByDescending(record => record.Id)
			.ToList();

		var liveGroups = HintSectionGroups.V1
			.Select(group => new HintSection
			{
				Group = group,
				Hints = subjectRecords
					.Where(record => record.Status == HintStatus.New
						&& HintSectionGroups.Resolve(record.Character).Id == group.Id)
					.ToList(),
			})
			.Where(section => section.Hints.Count > 0)
			.ToList();

		return new HintPanelData
		{
			Subject = subject,
			LiveGroups = liveGroups,
			History = subjectRecords
				.Where(record => record.Status != HintStatus.New)
				.ToList(),
		};
	}
}
