import { Link } from "react-router";

/**
 * Русифицированная страница ошибки сохраняет маршрут /Error из старого UI:
 * пользователь получает понятный fallback и явный возврат в журнал.
 * Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
 */
export function ErrorPage() {
	return (
		<section className="flex flex-col gap-3 p-6">
			<h1 className="text-2xl font-semibold tracking-tight">Ошибка журнала</h1>
			<p className="text-sm text-muted-foreground">
				Новый интерфейс не смог открыть запрошенный экран. Обновите страницу или вернитесь к списку конструкций.
			</p>
			<div>
				<Link to="/" className="text-sm text-primary underline-offset-4 hover:underline">
					Вернуться к конструкциям
				</Link>
			</div>
		</section>
	);
}
