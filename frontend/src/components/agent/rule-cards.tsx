import { ExternalLink } from "lucide-react";
import { StatusChip } from "@/components/design";
import type { AgentRuleCard, AgentRuleListItem } from "@/lib/api/agent-rules";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";

/*
	Подписи таксономии корпуса правил для пилюль-атрибутов и чипов фильтров
	мастера Body #7: характер — тот же закрытый справочник десяти значений,
	что и в подсказках (HintCharacterLabels); чёткость crisp/fuzzy — пара
	«Однозначное»/«Формальное» мастера; субъект construction/portfolio —
	«конструкция»/«журнал». Незнакомое значение показывается как есть.
*/
export const RULE_CHARACTER_LABELS: Readonly<Record<string, string>> = {
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

export const RULE_CLARITY_LABELS: Readonly<Record<string, string>> = {
	crisp: "Однозначное",
	fuzzy: "Формальное",
};

export const RULE_SUBJECT_LABELS: Readonly<Record<string, string>> = {
	construction: "конструкция",
	portfolio: "журнал",
};

const RULE_BODY_CLASS = "text-[13px] leading-[1.55] text-text-secondary";

export function ruleCharacterLabel(character: string): string {
	return RULE_CHARACTER_LABELS[character] ?? character;
}

function ruleClarityLabel(clarity: string): string {
	return RULE_CLARITY_LABELS[clarity] ?? clarity;
}

function ruleSubjectWord(subject: string): string {
	return RULE_SUBJECT_LABELS[subject] ?? subject;
}

/*
	«Карточка правила/Полная» (reusable Zes7z, инстансы R:1–R:4 Body #7):
	$surface, кайма $border, радиус 12, паддинги 18, вертикальный гэп 12.
	Шапка HLLTo (гэп 10): заголовок 14.5/600 $textPrimary + id 11/normal
	$textMuted. Пилюли-атрибуты Meta V3si6 (гэп 8) — примитив «Чип/Статус»
	(aa6cK, радиус 999, [4,10], 11.5/500): характер — пара $riskSoft/$risk
	(инстанс nrPzY «Мягкое»; пара $negSoft/$neg «Жёсткое» не имеет аналога
	в таксономии корпуса), чёткость — $infoSoft/$info (U6mVZ
	«Однозначное»), субъект — surface2/$textSecondary (u9Vm0h). Тело
	GjcwT — 13/normal $textSecondary, межстрочный 1.55. Футер p4Ot2 (гэп
	6): иконка external-link 12×12 + ссылка 12 $accentStrong «Открыть в
	каталоге в новом окне».
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2
export function RuleFullCard({
	rule,
	detail,
	onOpen,
}: {
	rule: Pick<AgentRuleListItem, "id" | "title" | "character" | "clarity" | "subject">;
	detail?: AgentRuleCard;
	onOpen?: () => void;
}) {
	return (
		<article data-slot="rule-full-card" className="flex flex-col gap-3 rounded-lg border bg-card p-[18px]">
			<header className="flex items-center gap-2.5">
				{onOpen === undefined ? (
					<h3 className="min-w-0 grow text-[14.5px] leading-snug font-semibold text-foreground">{rule.title}</h3>
				) : (
					<button
						type="button"
						className="min-w-0 grow cursor-pointer text-left text-[14.5px] leading-snug font-semibold text-foreground underline-offset-2 outline-none focus-visible:underline hover:underline"
						onClick={onOpen}
					>
						{rule.title}
					</button>
				)}
				<span className="shrink-0 text-[11px] text-text-muted">{rule.id}</span>
			</header>
			<div className="flex flex-wrap gap-2">
				<StatusChip tone="risk">{ruleCharacterLabel(rule.character)}</StatusChip>
				<StatusChip tone="info">{ruleClarityLabel(rule.clarity)}</StatusChip>
				<StatusChip tone="neutral">Субъект: {ruleSubjectWord(rule.subject)}</StatusChip>
			</div>
			{detail !== undefined && <RuleCardBody detail={detail} full={onOpen === undefined} />}
			<footer className="flex items-center gap-1.5">
				<ExternalLink aria-hidden="true" className="size-3 shrink-0 text-accent-strong" />
				<a
					href={`/spa/agent?rule=${encodeURIComponent(rule.id)}`}
					target="_blank"
					rel="noreferrer"
					className="text-xs text-accent-strong underline-offset-2 hover:underline"
				>
					Открыть в каталоге в новом окне
				</a>
			</footer>
		</article>
	);
}

/*
	Тело карточки: в каталоге — краткое содержание мастера GjcwT (одно
	описание действия/триггера), в правой панели — полные данные правила
	(триггер, действие, пороги, источники) той же типографикой мастера
	13/1.55 $textSecondary — доменное расширение состава карточки при
	сохранении вида мастера (Non-Goals: состав данных на месте).
*/
function RuleCardBody({ detail, full }: { detail: AgentRuleCard; full: boolean }) {
	const gist = detail.actionDescription ?? detail.triggerDescription;
	if (full === false) {
		return gist !== null ? <MarkdownViewer text={gist} className={RULE_BODY_CLASS} /> : null;
	}

	// Полная карточка источника показывает атрибуты, описания и цитаты
	// без сырого Markdown, тем же безопасным рендером, что и сообщения чата.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
	return (
		<div className="flex flex-col gap-2 text-[13px] leading-[1.55] text-text-secondary">
			<p><b>Статус:</b> {detail.status} · <b>Область:</b> {detail.scope}</p>
			{detail.technique !== null && <div><b>Техника:</b><MarkdownViewer text={detail.technique} className={RULE_BODY_CLASS} /></div>}
			{detail.triggerDescription !== null && <div><b>Триггер:</b><MarkdownViewer text={detail.triggerDescription} className={RULE_BODY_CLASS} /></div>}
			{detail.actionDescription !== null && <div><b>Действие:</b><MarkdownViewer text={detail.actionDescription} className={RULE_BODY_CLASS} /></div>}
			{detail.thresholds.length > 0 && (
				<ul className="list-disc pl-4">
					{detail.thresholds.map((threshold) => (
						<li key={`${threshold.name}-${threshold.unit}`}>
							{threshold.name}: {threshold.value} {threshold.unit}
						</li>
					))}
				</ul>
			)}
			{detail.sources.length > 0 && (
				<ul className="flex flex-col gap-1">
					{detail.sources.map((source) => (
						<li key={`${source.tag}-${source.file}`} className="text-[11px] text-text-muted">
							<span className="rounded bg-surface-2 px-1 py-0.5 font-medium">{source.tag}</span> {source.file}
							{source.quotes.map((quote, index) => <MarkdownViewer key={index} text={quote} className="text-[11px] text-text-muted" />)}
						</li>
					))}
				</ul>
			)}
		</div>
	);
}

/*
	«Попап правила/Краткий» (reusable Lpcap, инстанс IBheJ Body #4 плавает
	над лентой чата): $surface, кайма $border, радиус 10, паддинги 12,
	гэп 7, тень мастера 0 8 24 #17171E20 ($textPrimary при 12% непрозрач-
	ности — значение эффекта мастера, отдельного токена нет). Заголовок
	oYr9W — 12.5/600 $textPrimary; мета rlwtL — 11/normal $textMuted
	«характер · чёткость · субъект · id»; суть lidNU — 12/normal
	$textSecondary, межстрочный 1.45.
*/
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2
export function RuleBriefPopup({ card }: { card: AgentRuleCard }) {
	const gist = card.actionDescription ?? card.triggerDescription;
	return (
		<aside
			aria-label="Карточка правила (hover из чата)"
			className="pointer-events-none absolute right-3 top-3 z-10 flex w-[280px] max-w-full flex-col gap-[7px] rounded-[10px] border bg-card p-3 shadow-[0_8px_24px] shadow-text-primary/12"
		>
			<p className="text-[12.5px] font-semibold text-foreground">{card.title}</p>
			<p className="text-[11px] text-text-muted">
				{ruleCharacterLabel(card.character)} · {ruleClarityLabel(card.clarity)} · {ruleSubjectWord(card.subject)} · {card.id}
			</p>
			{/* Краткая карточка источника также следует общей Markdown-политике. */}
			{/* Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика */}
			{gist !== null && <MarkdownViewer text={gist} className="text-xs leading-[1.45] text-text-secondary" />}
		</aside>
	);
}
