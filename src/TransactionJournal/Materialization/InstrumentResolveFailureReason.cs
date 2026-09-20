namespace TransactionJournal.Materialization;

/// <summary>Причина, по которой символ опциона не удалось сверить со справочником инструментов.</summary>
public enum InstrumentResolveFailureReason
{
	/// <summary>Символ не соответствует формату символа опциона Bybit.</summary>
	MalformedOptionSymbol,

	/// <summary>Инструмента нет в справочнике — его спецификацию нужно загрузить из биржи.</summary>
	UnknownSymbol,

	/// <summary>Запись справочника принадлежит категории, отличной от option.</summary>
	CategoryMismatch,

	/// <summary>Базовый актив из символа расходится со справочником.</summary>
	BaseCoinMismatch,

	/// <summary>Тип опциона из символа расходится со справочником.</summary>
	OptionsTypeMismatch,

	/// <summary>Дата экспирации из символа расходится со временем delivery справочника.</summary>
	DeliveryTimeMismatch,
}
