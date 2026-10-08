import * as React from "react";
import type { HintPanel, HintRecord, HintStatus } from "@/lib/api/hints";
import { Button } from "@/components/ui/button";
import { formatMoment } from "@/lib/format/display-time";
import { cn } from "@/lib/utils";

// Панель подсказок правой области раздела по концепции §3: живые подсказки
// группами справочника v1, карточки полного состава с кнопками «Применено»/
// «Отклонено», свёрнутая терминальная история и явное пустое состояние.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Пропсы панели подсказок. */
export interface HintsPanelProps {
	/** Панель субъекта из API: группы живых и терминальная история. */
	readonly panel: HintPanel;
	/** Команда «Применено» — перевод живой подсказки в applied. */
	readonly onApply: (hintId: number) => void;
	/** Команда «Отклонено» — перевод живой подсказки в dismissed. */
	readonly onDismiss: (hintId: number) => void;
	/** Дополнительный класс контейнера панели. */
	readonly className?: string;
}

/** Подписи характеров действия таксономии v1; неизвестный характер — как есть. */
const CHARACTER_LABELS: Readonly<Record<string, string>> = {
	"risk-mode": "лимиты и режим риска",
	"profit-target": "цель по прибыли",
	"profit-protection": "защита прибыли",
	"risk-reduction": "снижение риска",
	rolling: "роллирование",
	entry: "возможность входа",
	exit: "возможность выхода",
	"futures-leg": "фьючерсная нога",
	"rebuild-dismantle": "перестройка и разборка",
	other: "прочее",
};

/** Статус записи словами карточки. */
function statusText(status: HintStatus): string {
	switch (status) {
		case "new":
			return "живая";
		case "applied":
			return "применена";
		case "dismissed":
			return "отклонена";
		case "expired":
			return "погашена";
	}
}

export function HintsPanel({ panel, onApply, onDismiss, className }: HintsPanelProps) {
	const [historyOpen, setHistoryOpen] = React.useState(false);
	const isEmpty = panel.liveCount === 0 && panel.history.length === 0;

	return (
		<section data-slot="hints-panel" className={cn("flex flex-col gap-3", className)}>
			{isEmpty ? (
				// Пустая панель — явное сообщение, а не пустая разметка.
				<p className="text-muted-foreground text-sm">подсказок нет</p>
			) : (
				<>
					{panel.liveGroups.map((section) => (
						// Группы справочника v1: пустые группы в панель не входят.
						<div key={section.group.id} className="flex flex-col gap-2">
							<h3 className="text-muted-foreground text-xs font-semibold tracking-wide uppercase">
								{section.group.title}
							</h3>
							{section.hints.map((hint) => (
								<HintCard key={hint.id} hint={hint} onApply={onApply} onDismiss={onDismiss} />
							))}
						</div>
					))}

					{panel.history.length > 0 && (
						<div className="flex flex-col gap-2">
							<Button
								variant="ghost"
								size="sm"
								className="text-muted-foreground self-start"
								aria-expanded={historyOpen}
								onClick={() => setHistoryOpen((open) => !open)}
							>
								{historyOpen ? `Свернуть историю (${panel.history.length})` : `История (${panel.history.length})`}
							</Button>
							{historyOpen &&
								// Терминальная история без кнопок: переходов из
								// терминальных статусов нет.
								panel.history.map((hint) => <HintCard key={hint.id} hint={hint} onApply={onApply} onDismiss={onDismiss} />)}
						</div>
					)}
				</>
			)}
		</section>
	);
}

/** Карточка полного состава подсказки: текст, атрибуты, факты, источники, действия. */
function HintCard({
	hint,
	onApply,
	onDismiss,
}: {
	hint: HintRecord;
	onApply: (hintId: number) => void;
	onDismiss: (hintId: number) => void;
}) {
	const isLive = hint.status === "new";

	return (
		<article
			data-slot="hint-card"
			data-status={hint.status}
			className="bg-card flex flex-col gap-2 rounded-md border p-3 shadow-xs"
		>
			<p className="text-sm leading-snug">{hint.text}</p>
			<dl className="text-muted-foreground grid grid-cols-2 gap-x-3 gap-y-0.5 text-xs">
				<div className="contents">
					<dt>характер</dt>
					<dd>{CHARACTER_LABELS[hint.character] ?? hint.character}</dd>
				</div>
				<div className="contents">
					<dt>чёткость</dt>
					<dd>{hint.clarity}</dd>
				</div>
				<div className="contents">
					<dt>правило</dt>
					<dd>{hint.ruleId}</dd>
				</div>
				<div className="contents">
					<dt>as-of</dt>
					<dd>{formatMoment(hint.asOf)}</dd>
				</div>
				<div className="contents">
					<dt>статус</dt>
					<dd>{statusText(hint.status)}</dd>
				</div>
			</dl>
			{Object.keys(hint.facts).length > 0 && (
				<ul className="text-muted-foreground text-xs">
					{Object.entries(hint.facts).map(([key, value]) => (
						<li key={key}>
							<span className="font-medium">{key}</span> {value}
						</li>
					))}
				</ul>
			)}
			{hint.sources.length > 0 && (
				<ul className="text-muted-foreground flex flex-col gap-1 text-xs">
					{hint.sources.map((source) => (
						<li key={`${source.tag}-${source.file}`}>
							<span className="bg-secondary text-secondary-foreground rounded px-1 py-px font-medium">{source.tag}</span>{" "}
							<span className="font-mono text-[11px]">{source.file}</span>
							<ul className="list-disc pl-4">
								{source.quotes.map((quote) => (
									<li key={quote}>{quote}</li>
								))}
							</ul>
						</li>
					))}
				</ul>
			)}
			{isLive && (
				// Кнопки завершения жизненного цикла — только у живой подсказки;
				// других мутаций подсказок UI не предоставляет.
				<div className="flex gap-2">
					<Button size="sm" onClick={() => onApply(hint.id)}>
						Применено
					</Button>
					<Button size="sm" variant="outline" onClick={() => onDismiss(hint.id)}>
						Отклонено
					</Button>
				</div>
			)}
		</article>
	);
}
