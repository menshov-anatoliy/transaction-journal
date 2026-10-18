using System.Globalization;
using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.Analytics;

/// <summary>Причина предупреждения диагностики незакрытых рисков конструкции.</summary>
public enum ConstructionRiskReason
{
	/// <summary>Риск не ограничен: убыток незакрытой ноги не имеет предела.</summary>
	UnboundedLoss,

	/// <summary>Убыток ограничен ценой входа: незакрытая длинная фьючерсная нога.</summary>
	BoundedByEntryPrice,

	/// <summary>Убыток ограничен нулём цены: незакрытый короткий пут.</summary>
	BoundedByZeroPrice,

	/// <summary>Покрытие не проверено: символ открытого остатка не распознан.</summary>
	UnverifiedCoverage,
}

/// <summary>
/// Предупреждение диагностики незакрытых рисков конструкции: запись называет
/// инструмент, число незакрытых контрактов («1 из 2 контрактов») и причину
/// на русском языке; неограниченный вкус в тексте называется каноническим
/// термином «Риск не ограничен». Записи вычисляются на лету рядом с диагнозом
/// хвостов и хранением не живут.
// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-messages
/// </summary>
public sealed record ConstructionRiskDiagnostic
{
	private ConstructionRiskDiagnostic(
		string symbol,
		decimal? uncoveredContracts,
		decimal? totalContracts,
		ConstructionRiskReason reason,
		string text)
	{
		Symbol = symbol;
		UncoveredContracts = uncoveredContracts;
		TotalContracts = totalContracts;
		Reason = reason;
		Text = text;
	}

	/// <summary>Инструмент с незакрытым риском или неразборчивым символом.</summary>
	public string Symbol { get; }

	/// <summary>Число незакрытых контрактов инструмента; у непроверенного покрытия отсутствует.</summary>
	public decimal? UncoveredContracts { get; }

	/// <summary>Полное число контрактов инструмента с открытым остатком; у непроверенного покрытия отсутствует.</summary>
	public decimal? TotalContracts { get; }

	/// <summary>Причина предупреждения.</summary>
	public ConstructionRiskReason Reason { get; }

	/// <summary>Готовый текст записи на русском языке.</summary>
	public string Text { get; }

	/// <summary>Предупреждение о незакрытой фьючерсной части инструмента.</summary>
	internal static ConstructionRiskDiagnostic UncoveredFuture(
		string symbol,
		decimal uncoveredContracts,
		decimal totalContracts,
		ConstructionRiskReason reason) =>
		new(
			symbol,
			uncoveredContracts,
			totalContracts,
			reason,
			$"{symbol}: фьючерс не закрыт опционами ({FormatCount(uncoveredContracts, totalContracts)}) — {ReasonPhrase(reason)}.");

	/// <summary>Предупреждение о незакрытой короткой опционной ноге инструмента.</summary>
	internal static ConstructionRiskDiagnostic UncoveredShortOption(
		string symbol,
		decimal uncoveredContracts,
		decimal totalContracts,
		OptionType optionType,
		ConstructionRiskReason reason) =>
		new(
			symbol,
			uncoveredContracts,
			totalContracts,
			reason,
			$"{symbol}: короткий {OptionWord(optionType)} не закрыт по рискам ({FormatCount(uncoveredContracts, totalContracts)}) — {ReasonPhrase(reason)}.");

	/// <summary>Предупреждение о неразборчивом символе открытого остатка.</summary>
	internal static ConstructionRiskDiagnostic UnverifiedCoverage(string symbol) =>
		new(symbol, null, null, ConstructionRiskReason.UnverifiedCoverage, $"{symbol}: символ не распознан — покрытие не проверено.");

	#region Вспомогательные методы

	/// <summary>Русская формулировка причины предупреждения.</summary>
	private static string ReasonPhrase(ConstructionRiskReason reason) => reason switch
	{
		// Канонический термин неограниченного вкуса переиспользуется из реального риска;
		// отдельные термины по типу ноги не вводятся.
		// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-messages
		ConstructionRiskReason.UnboundedLoss => "Риск не ограничен",
		ConstructionRiskReason.BoundedByEntryPrice => "убыток ограничен ценой входа",
		ConstructionRiskReason.BoundedByZeroPrice => "убыток ограничен нулём цены",
		_ => "покрытие не проверено",
	};

	/// <summary>Название типа опциона в тексте записи.</summary>
	private static string OptionWord(OptionType optionType) => optionType == OptionType.Call ? "колл" : "пут";

	/// <summary>Число незакрытых контрактов в форме «1 из 2 контрактов».</summary>
	private static string FormatCount(decimal uncoveredContracts, decimal totalContracts) =>
		$"{FormatNumber(uncoveredContracts)} из {FormatNumber(totalContracts)} {ContractsWord(totalContracts)}";

	/// <summary>Число без хвостовых нулей в инвариантном формате.</summary>
	private static string FormatNumber(decimal value) =>
		value.ToString("0.############################", CultureInfo.InvariantCulture);

	/// <summary>Слово «контракт» в родительном падеже для числа контрактов: «из 1 контракта», «из 2 контрактов».</summary>
	private static string ContractsWord(decimal totalContracts)
	{
		var isIntegral = totalContracts == decimal.Truncate(totalContracts);
		if (isIntegral == false)
		{
			return "контракта";
		}

		var whole = (long)totalContracts;
		var tens = Math.Abs(whole % 100);
		var units = Math.Abs(whole % 10);
		return units == 1 && tens != 11 ? "контракта" : "контрактов";
	}

	#endregion
}
