namespace TransactionJournal.Hints.Engine;

using TransactionJournal.Hints.Ports;

/// <summary>
/// Машинный триггер корпуса — чистая функция «(снапшот, марки, карточка) →
/// факты или нет срабатывания». Реестр собирает триггеры по ключам
/// trigger.implementation; новый ключ добавляется кодом движка вместе с
/// реализацией интерфейса, корпус сам код не меняет.
/// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public interface IHintTrigger
{
	/// <summary>Ключ реализации триггера — значение trigger.implementation карточки.</summary>
	string Key { get; }

	/// <summary>
	/// Вид субъекта правила: журнальные правила оцениваются один раз по всему
	/// снимку (субъект «журнал»), правила конструкций — по каждой открытой
	/// конструкции (субъект «конструкция» со ссылкой на неё).
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-subject-v1-closed-set
	/// </summary>
	HintSubjectKind SubjectKind { get; }

	/// <summary>Вычисляет условие правила по входам прохода; пороги берутся только из thresholds карточки.</summary>
	/// <param name="input">Вход оценки: снимок, марки, карточка, as-of, конструкция-субъект.</param>
	/// <returns>Исход оценки с фактами шаблона при срабатывании.</returns>
	TriggerOutcome Evaluate(TriggerEvaluationInput input);
}
