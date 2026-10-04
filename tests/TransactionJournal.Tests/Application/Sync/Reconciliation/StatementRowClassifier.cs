using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
namespace TransactionJournal.Tests.Application.Sync.Reconciliation;

/// <summary>
/// Классификация типов строк выгрузки: какие типы сверяются с журналом,
/// какие лежат вне области синхронизации, а какие сверке неизвестны.
/// </summary>
public static class StatementRowClassifier
{
	/// <summary>
	/// Определяет категорию строки выгрузки для сверки.
	/// </summary>
	/// <param name="row">Строка выгрузки.</param>
	/// <returns>Категория строки.</returns>
	public static StatementRowCategory Classify(StatementRow row)
	{
		ArgumentNullException.ThrowIfNull(row);

		// Все строки fund-файла журнал не загружает: операции фонда всегда вне области
		// сверки, независимо от типа (Earn, Deposit, Withdraw, Transfer in/out).
		// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-out-of-scope-rows-summary
		if (row.FileKind == StatementFileKind.Fund)
		{
			return StatementRowCategory.OutOfScope;
		}

		return row.Type switch
		{
			// Сделки сверяются с сырыми записями исполнения журнала.
			// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-trade-rows-match-executions
			"TRADE" => StatementRowCategory.Trade,

			// Экспирации сверяются с сырыми delivery-записями журнала.
			// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-delivery-rows-match-deliveries
			"DELIVERY" => StatementRowCategory.Delivery,

			// Фандинг и переводы журнал не загружает: строки попадают в сводку вне области.
			// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-funding-and-transfers-summarized
			"SETTLEMENT" or "TRANSFER_IN" or "TRANSFER_OUT" => StatementRowCategory.OutOfScope,

			// Неизвестный тип UTA-файла не скрывается молча: он обязан быть виден в сводке отчёта.
			// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-unknown-type-listed-in-summary
			_ => StatementRowCategory.Unknown,
		};
	}
}
