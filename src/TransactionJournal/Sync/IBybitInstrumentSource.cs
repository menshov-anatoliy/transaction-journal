using TransactionJournal.Bybit;

namespace TransactionJournal.Sync;

/// <summary>
/// Порт публичного справочника спецификаций инструментов для подсистемы синхронизации:
/// отдельный интерфейс рядом с шлюзом истории, чтобы пополнение справочника проверялось
/// на фиктивном источнике без сети. Порт только читает публичные спецификации —
/// методов, изменяющих торговый аккаунт, здесь нет.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// Traceability: openspec:sync/bybit-history#requirement-read-only-access
/// </summary>
public interface IBybitInstrumentSource
{
	/// <summary>
	/// GET /v5/market/instruments-info — страница спецификаций инструментов категории
	/// с курсорной пагинацией; фильтр по символу возвращает спецификацию одного инструмента.
	/// </summary>
	/// <param name="query">Параметры запроса: категория, фильтр символа, размер страницы, курсор.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	Task<BybitPagedResponse<BybitInstrumentInfo>> GetInstrumentInfoAsync(
		BybitInstrumentInfoQuery query,
		CancellationToken cancellationToken = default);
}
