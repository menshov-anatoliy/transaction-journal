import { useQuery } from "@tanstack/react-query";
import { ChevronDown } from "lucide-react";
import * as React from "react";
import { Link } from "react-router";
import { StatusChip } from "@/components/design";
import { Button } from "@/components/ui/button";
import {
	fetchHintsLog,
	type HintGroupDefinition,
	type HintLogFilter,
	type HintLogRecord,
	type HintStatus,
} from "@/lib/api/hints";
import { formatMoment } from "@/lib/format/display-time";
import { cn } from "@/lib/utils";

// Раздел «Подсказки» показывает read-only журнал всех подсказок всех
// субъектов: фильтры статус/группа/характер, отдельные состояния «записей нет»
// и «по фильтру записей нет», карточки без кнопок мутации.
// Traceability: doc:.wf-research/ui-concept/concept.md#6-раздел-подсказки-маршрут-hints
// Перенос №15 фиксирует выделенный маршрут журнала подсказок `/hints`.
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

/*
	Экран перенесён с мастера Body #6 «Подсказки» (LmLr6) макета design.pen
	(расхождения §3.7: 1–3 аудита): журнал подсказок — сетка карточек
	«Карточка подсказки» (reusable H0y2H) вместо таблицы. Шапка — титул
	21/600 + счётчик 12.5/normal $textSecondary (tpZfS/pdFeV); фильтры
	(ccdID) — сегмент-контрол статуса (btUq8: контейнер $surface r9 [3] +
	табы r7 [6,12], активный accentSoft/accentStrong 12/600) и поля-дропдауны
	группы/характера (wgKkr/XyeI5: $surface, r8, [7,10], Inter 12/normal
	$textPrimary, шеврон 13×13 $textMuted) на нативных select; сброс —
	Ghost-инстанс Kn5dY ([6,10], 12). Список (SVwot) — ряды по две карточки
	с гэпом 12. Статус карточки — примитив StatusChip: «Отклонено» мастера
	перекрашено в пару $negSoft/$neg (тон neg).
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2

const hintsLogKey = (filter: HintLogFilter) => ["hints-log", filter] as const;

const STATUS_TABS: ReadonlyArray<{ value: HintStatus | ""; label: string }> = [
	{ value: "", label: "Все" },
	{ value: "new", label: "живая" },
	{ value: "applied", label: "применена" },
	{ value: "dismissed", label: "отклонена" },
	{ value: "expired", label: "погашена" },
];

const GROUP_OPTIONS: readonly HintGroupDefinition[] = [
	{ id: "risk-mode", title: "Риск-режим" },
	{ id: "construction-management", title: "Управление конструкцией" },
	{ id: "futures-leg", title: "Фьючерсная нога" },
];

const GROUP_CHARACTERS: Readonly<Record<string, readonly string[]>> = {
	"risk-mode": ["risk-mode"],
	"construction-management": ["exit", "profit-protection", "profit-target", "risk-reduction", "rolling", "rebuild-dismantle", "other"],
	"futures-leg": ["futures-leg"],
};

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

const FALLBACK_CHARACTER_ORDER = [
	"risk-mode",
	"profit-target",
	"profit-protection",
	"risk-reduction",
	"rolling",
	"entry",
	"exit",
	"futures-leg",
	"rebuild-dismantle",
	"other",
] as const;

const DEFAULT_LIMIT = 200;

export function HintsPage() {
	const [status, setStatus] = React.useState<HintStatus | "">("");
	const [groupId, setGroupId] = React.useState("");
	const [character, setCharacter] = React.useState("");

	const filter = React.useMemo<HintLogFilter>(
		() => ({
			status: status === "" ? undefined : status,
			group: groupId.length > 0 ? groupId : undefined,
			character: character.length > 0 ? character : undefined,
			limit: DEFAULT_LIMIT,
		}),
		[status, groupId, character],
	);

	const query = useQuery({
		queryKey: hintsLogKey(filter),
		queryFn: () => fetchHintsLog(filter),
	});

	const availableCharacters = React.useMemo(() => {
		if (groupId.length > 0) {
			return GROUP_CHARACTERS[groupId] ?? [];
		}

		const seen = new Set<string>(FALLBACK_CHARACTER_ORDER);
		for (const entry of query.data?.items ?? []) {
			seen.add(entry.character);
		}

		return [...seen];
	}, [groupId, query.data?.items]);

	React.useEffect(() => {
		if (groupId.length == 0 || character.length == 0) {
			return;
		}

		const allowed = GROUP_CHARACTERS[groupId] ?? [];
		if (allowed.includes(character) == false) {
			setCharacter("");
		}
	}, [groupId, character]);

	const hasActiveFilter = status.length > 0 || groupId.length > 0 || character.length > 0;

	return (
		<section className="flex flex-col gap-3.5 px-7 py-5 pb-6">
			{/*
				Шапка мастера (uQt8l): титул «Подсказки» 21/600 + счётчик записей
				12.5/normal $textSecondary (pdFeV «журнал всех субъектов · 24
				записи»); приложение показывает в том же слоте диапазон выборки.
			*/}
			<div className="flex items-center gap-3">
				<h1 className="page-title">Подсказки</h1>
				{query.data !== undefined && (
					<span className="text-[12.5px] text-text-secondary">
						журнал всех субъектов · показано {query.data.items.length} из {query.data.total}
					</span>
				)}
			</div>

			{/*
				Строка фильтров перенесена с дизайн-ноды «Фильтры» (ccdID) Body #6:
				сегмент-контрол статуса (btUq8) с активным табом accentSoft/
				accentStrong, поля группы/характера (wgKkr/XyeI5) с шевроном и
				Ghost-кнопка сброса (ciwcY). Нативные select сохраняют семантику
				и стилизуются геометрией поля мастера.
			*/}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			<div className="flex flex-wrap items-center gap-2.5">
				<div
					role="radiogroup"
					aria-label="Фильтр по статусу"
					className="inline-flex items-center gap-1 rounded-[9px] border bg-card p-[3px]"
				>
					{STATUS_TABS.map((tab) => {
						const active = status === tab.value;
						return (
							<button
								key={tab.value}
								type="button"
								role="radio"
								aria-checked={active}
								className={cn(
									"rounded-[7px] px-3 py-1.5 text-xs transition-colors",
									active
										? "bg-accent-soft font-semibold text-accent-strong"
										: "text-text-secondary hover:text-foreground",
								)}
								onClick={() => setStatus(tab.value)}
							>
								{tab.label}
							</button>
						);
					})}
				</div>

				<SelectField label="Фильтр по группе" allLabel="Группа: все" value={groupId} onChange={setGroupId}>
					{GROUP_OPTIONS.map((option) => (
						<option key={option.id} value={option.id}>
							Группа: {option.title}
						</option>
					))}
				</SelectField>

				<SelectField
					label="Фильтр по характеру"
					allLabel="Характер: все"
					value={character}
					onChange={setCharacter}
				>
					{availableCharacters.map((value) => (
						<option key={value} value={value}>
							Характер: {characterLabel(value)}
						</option>
					))}
				</SelectField>

				{hasActiveFilter && (
					<Button
						variant="ghost"
						className="h-auto px-2.5 py-1.5 text-xs"
						onClick={() => {
							setStatus("");
							setGroupId("");
							setCharacter("");
						}}
					>
						Сбросить фильтры
					</Button>
				)}
			</div>

			{query.isPending && <p className="text-muted-foreground text-sm">чтение подсказок…</p>}
			{query.isError && (
				<p className="text-destructive text-sm" role="alert">
					Журнал подсказок недоступен: {query.error.message}
				</p>
			)}
			{query.data !== undefined && query.data.items.length === 0 && (
				<p className="text-muted-foreground text-sm">{hasActiveFilter ? "по фильтру записей нет" : "записей нет"}</p>
			)}

			{query.data !== undefined && query.data.items.length > 0 && (
				// Список мастера (SVwot): ряды по две карточки, гэп 12 между
				// карточками и рядами; на узких экранах — одна колонка.
				<ul className="grid items-start gap-3 sm:grid-cols-2">
					{query.data.items.map((item) => (
						<HintLogCard key={item.id} item={item} />
					))}
				</ul>
			)}
		</section>
	);
}

/*
	Поле-дропдаун фильтра (ноды wgKkr «Group» / XyeI5 «Char» мастера):
	белая поверхность $surface, кайма $border, радиус 8, паддинги [7,10],
	Inter 12/normal $textPrimary и шеврон chevron-down 13×13 $textMuted
	справа. Нативный select сохраняет доступность и клавиатурный ввод,
	его системная стрелка скрыта, шеврон мастера — абсолютным слоем.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2
function SelectField({
	label,
	allLabel,
	value,
	onChange,
	children,
}: {
	label: string;
	allLabel: string;
	value: string;
	onChange: (value: string) => void;
	children: React.ReactNode;
}) {
	return (
		<label className="relative inline-flex items-center">
			<select
				aria-label={label}
				className="min-w-0 cursor-pointer appearance-none rounded-sm border bg-card py-[7px] pl-2.5 pr-[31px] text-xs text-foreground outline-none"
				value={value}
				onChange={(event) => onChange(event.currentTarget.value)}
			>
				<option value="">{allLabel}</option>
				{children}
			</select>
			<ChevronDown aria-hidden="true" className="pointer-events-none absolute right-2.5 size-[13px] text-text-muted" />
		</label>
	);
}

/*
	Карточка журнала перенесена с reusable-ноды «Карточка подсказки» (H0y2H):
	$surface, кайма $border, радиус 12, паддинги 14, гэп 8, вертикальная
	компоновка. Шапка (rMNQ6): группа Inter 12.5/600 $textPrimary + чип
	статуса; текст (urdBT) — Inter 12.5/normal $textSecondary, межстрочный
	1.5; сноска-подвал (QEiI8) — Inter 11/normal $textMuted «правило … ·
	показана впервые …». Субъект/характер/as-of, факты и источники —
	доменное расширение той же типографики сноски (11 $textMuted).
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2
function HintLogCard({ item }: { item: HintLogRecord }) {
	return (
		<li>
			<article data-slot="hint-card" data-status={item.status} className="flex flex-col gap-2 rounded-lg border bg-card p-3.5">
				<div className="flex items-center gap-2">
					<h3 className="text-[12.5px] font-semibold text-foreground">{item.group.title}</h3>
					<span aria-hidden="true" className="grow" />
					<StatusChip tone={statusTone(item.status)}>{statusLabel(item.status)}</StatusChip>
				</div>

				<p className="text-[12.5px] leading-[1.5] text-text-secondary">{item.text}</p>

				<p className="text-[11px] text-text-muted">
					{renderSubject(item)} · {characterLabel(item.character)} · {formatMoment(item.asOf)}
				</p>

				{Object.keys(item.facts).length > 0 && (
					<ul className="list-disc pl-4 text-[11px] text-text-muted">
						{Object.entries(item.facts).map(([key, value]) => (
							<li key={key}>
								<span className="font-medium">{key}</span> {value}
							</li>
						))}
					</ul>
				)}

				{item.sources.length > 0 && (
					// Источники — списочная типографика мастера для файлов: Inter
					// ($font) 11/normal $textMuted вместо моноширинного font-mono
					// (моно в дизайн-системе — только JetBrains Mono в редакторе).
					// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
					<ul className="flex flex-col gap-1 text-[11px] text-text-muted">
						{item.sources.map((source) => (
							<li key={`${item.id}:${source.tag}:${source.file}`} className="space-y-0.5">
								<div className="flex flex-wrap items-center gap-1">
									<span className="rounded bg-surface-2 px-1 py-0.5 font-medium">{source.tag}</span>
									<span>{source.file}</span>
								</div>
								{source.quotes.length > 0 && (
									<ul className="list-disc pl-4">
										{source.quotes.map((quote) => (
											<li key={quote}>{quote}</li>
										))}
									</ul>
								)}
							</li>
						))}
					</ul>
				)}

				<p className="text-[11px] text-text-muted">
					правило {item.ruleId} · показана впервые {formatMoment(item.firstSeenAt ?? item.asOf)}
				</p>
			</article>
		</li>
	);
}

function renderSubject(item: HintLogRecord) {
	if (item.subject.kind === "journal") {
		return "журнал";
	}

	if (item.subject.constructionId === null) {
		return "конструкция";
	}

	return (
		<Link className="underline decoration-dotted underline-offset-3" to={`/constructions/${item.subject.constructionId}`}>
			конструкция {item.subject.constructionId}
		</Link>
	);
}

/*
	Тон чипа статуса по инстансам мастера: «Применено» — pos
	(accentSoft/accentStrong), «Отклонено» — neg (negSoft/neg, Body #6);
	живая — pos (чипы «живая» Body #1); для «погашена» мастер-ноды нет —
	терминальный статус показан приглушённым тоном muted.
*/
function statusTone(status: HintStatus): "pos" | "neg" | "muted" {
	switch (status) {
		case "new":
			return "pos";
		case "applied":
			return "pos";
		case "dismissed":
			return "neg";
		case "expired":
			return "muted";
	}
}

function statusLabel(status: HintStatus): string {
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

function characterLabel(character: string): string {
	return CHARACTER_LABELS[character] ?? character;
}
