using TransactionJournal.Materialization;

namespace TransactionJournal.Sync;

/// <summary>
/// Сервис команды «Переразобрать сырые записи заново» экрана «Настройки»: полная
/// пересборка доменных представлений журнала из локального сырья без сетевых
/// запросов к бирже. Возвращает согласованную проекцию со счётчиками записей и
/// предупреждениями сверки для показа итога пользователю.
/// Traceability: openspec:ui/screens#scenario-settings-reparse-confirmation
/// </summary>
public interface IJournalReparseService
{
	/// <summary>
	/// Полностью перестраивает доменные представления журнала из сырых записей
	/// хранилища: сделки «Входящих», закрывающие записи экспираций и предупреждения
	/// сверки. Ошибка разбора повреждённого сырья проходит наружу к экрану.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены переразбора.</param>
	Task<JournalMaterializationResult> ReparseAsync(CancellationToken cancellationToken = default);
}

/// <summary>Реализация команды поверх снимка сырых записей и материализатора журнала.</summary>
public sealed class JournalReparseService : IJournalReparseService
{
	private readonly IJournalRawSnapshotStore _rawSnapshotStore;
	private readonly JournalMaterializer _materializer;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт команду переразбора над сырым хранилищем и материализатором.</summary>
	/// <param name="rawSnapshotStore">Источник полного снимка сырых записей журнала.</param>
	/// <param name="materializer">Переразборщик доменных представлений из сырых записей.</param>
	/// <param name="timeProvider">Поставщик времени для «как сейчас»-OTM; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Какая-либо обязательная зависимость не задана.</exception>
	public JournalReparseService(
		IJournalRawSnapshotStore rawSnapshotStore,
		JournalMaterializer materializer,
		TimeProvider? timeProvider = null)
	{
		_rawSnapshotStore = rawSnapshotStore ?? throw new ArgumentNullException(nameof(rawSnapshotStore));
		_materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc cref="IJournalReparseService.ReparseAsync" />
	public async Task<JournalMaterializationResult> ReparseAsync(CancellationToken cancellationToken = default)
	{
		// Переразбор работает только над локальным сырьём: снимок читается целиком,
		// материализатор строит конвейер проекции заново — смена правила разбора
		// применяется пересборкой без следов прежних правил и без сетевых запросов.
		// Привязки сделок здесь не подставляются: итог команды — проверка согласованности
		// сырья и счётчики, привязки к конструкциям при чтении применяют read-модели.
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		var snapshot = await _rawSnapshotStore.LoadAsync(cancellationToken).ConfigureAwait(false);
		return _materializer.Materialize(
			snapshot.Instruments,
			snapshot.Executions,
			snapshot.Deliveries,
			tradeAssignments: null,
			asOf: _timeProvider.GetUtcNow());
	}
}
