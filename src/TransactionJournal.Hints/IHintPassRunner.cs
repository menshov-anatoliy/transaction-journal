namespace TransactionJournal.Hints;

/// <summary>
/// Запуск прохода агента подсказок — контракт потребителя UI: кнопка ручного
/// запуска выполняет проход по текущим журналу, котировкам и снимку корпуса тем
/// же кодом, каким пойдут плановые проходы. Исход прохода с диагностикой
/// вернёт HintPassResult.
// Traceability: openspec:ui/screens#requirement-ui-manual-pass-button
/// </summary>
public interface IHintPassRunner
{
	/// <summary>Выполняет проход агента над текущими журналом, котировками и корпусом.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<HintPassResult> RunAsync(CancellationToken cancellationToken = default);
}
