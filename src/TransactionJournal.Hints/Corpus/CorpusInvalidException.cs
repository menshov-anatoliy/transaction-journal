namespace TransactionJournal.Hints.Corpus;

/// <summary>
/// Агрегированная ошибка невалидного корпуса: непарсируемый YAML, несоответствие
/// схеме, отсутствующий или пустой каталог, две активные карточки объявленной
/// конфликтной пары. Содержит полный список проблем всех карточек за одно
/// чтение — владелец приводит корпус в порядок до запуска агента; проход до
/// построения снимка и любых чтений журнала и рынка не доходит.
/// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
/// </summary>
public sealed class CorpusInvalidException : Exception
{
	/// <summary>Все проблемы корпуса, собранные за одно чтение каталога.</summary>
	public IReadOnlyList<string> Problems { get; }

	/// <summary>Создаёт агрегированную ошибку по полному списку проблем.</summary>
	/// <param name="problems">Проблемы всех битых карточек и каталога.</param>
	public CorpusInvalidException(IReadOnlyList<string> problems)
		: base($"Корпус правил невалиден, проблем: {problems.Count}. " + string.Join(" ", problems))
	{
		if (problems.Count == 0)
		{
			throw new ArgumentException("Список проблем невалидного корпуса не может быть пустым.", nameof(problems));
		}

		Problems = problems;
	}
}
