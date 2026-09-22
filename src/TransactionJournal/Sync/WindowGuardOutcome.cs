namespace TransactionJournal.Sync;

/// <summary>
/// Исход защитного контура перебора истории: успешный проход окна, пограничное
/// исчерпание перебора либо недоступность области для запроса.
/// </summary>
internal enum WindowGuardOutcome
{
	/// <summary>Окно пройдено: результат прохода заполнен.</summary>
	Passed,

	/// <summary>Пограничное исчерпание перебора: доступная история исчерпана, оставшиеся окна не запрашиваются.</summary>
	BoundaryExhausted,

	/// <summary>Область недоступна: биржа отказала в выдаче истории контракта, обход окон области прекращается.</summary>
	AreaUnavailable,
}

/// <summary>
/// Результат защищённого прохода окна: исход перебора и, при успешном проходе,
/// результат прохода окна.
/// </summary>
internal readonly record struct WindowGuardResult<TPassResult>(WindowGuardOutcome Outcome, TPassResult? PassResult)
	where TPassResult : class
{
	/// <summary>Исход успешного прохода окна с результатом прохода.</summary>
	public static WindowGuardResult<TPassResult> Passed(TPassResult passResult)
	{
		return new WindowGuardResult<TPassResult>(WindowGuardOutcome.Passed, passResult);
	}

	/// <summary>Исход пограничного исчерпания перебора без результата прохода.</summary>
	public static WindowGuardResult<TPassResult> BoundaryExhausted()
	{
		return new WindowGuardResult<TPassResult>(WindowGuardOutcome.BoundaryExhausted, null);
	}

	/// <summary>Исход недоступности области без результата прохода.</summary>
	public static WindowGuardResult<TPassResult> AreaUnavailable()
	{
		return new WindowGuardResult<TPassResult>(WindowGuardOutcome.AreaUnavailable, null);
	}
}
