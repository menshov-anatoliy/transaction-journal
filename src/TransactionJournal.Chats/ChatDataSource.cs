namespace TransactionJournal.Chats;

/// <summary>
/// Источник данных чата агента — категория закрытого справочника, только для
/// чтения: журнал сделок, корпус правил и рынок Bybit. Категорий ровно три,
/// ничего сверх справочника у чата нет; пишущих источников не существует.
/// Набор источников — параметр чата: владелец выбирает подмножество при
/// создании, по умолчанию доступны все три категории.
// Traceability: openspec:chats/sources#requirement-sources-closed-catalog
// Traceability: openspec:chats/sources#scenario-sources-write-never
/// </summary>
public enum ChatDataSource
{
	/// <summary>Журнал сделок: факты приходят детерминированным снимком контекста, инструментами не читается.</summary>
	Journal,

	/// <summary>Корпус правил: карточки читаются инструментом read_rule_card.</summary>
	RulesCorpus,

	/// <summary>Рынок Bybit: снимок фьючерсного рынка и доска опционов — рыночные инструменты.</summary>
	BybitMarket,
}

/// <summary>
/// Закрытый справочник источников данных чата: полный состав — ровно три
/// категории в каноническом порядке (журнал, корпус правил, рынок Bybit).
/// Полный набор служит дефолтом набора источников чата.
// Traceability: openspec:chats/sources#scenario-sources-three-categories
/// </summary>
public static class ChatDataSourceCatalog
{
	/// <summary>Полный набор источников — все три категории в каноническом порядке; он же дефолт чата.</summary>
	public static IReadOnlyList<ChatDataSource> All { get; } =
	[
		ChatDataSource.Journal,
		ChatDataSource.RulesCorpus,
		ChatDataSource.BybitMarket,
	];
}
