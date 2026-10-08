import { useQuery } from "@tanstack/react-query";
import * as React from "react";
import { Link } from "react-router";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
	fetchHintsLog,
	type HintGroupDefinition,
	type HintLogFilter,
	type HintLogRecord,
	type HintStatus,
} from "@/lib/api/hints";
import { formatMoment } from "@/lib/format/display-time";
import { useIsMobile } from "@/lib/use-mobile";

// Раздел «Подсказки» показывает read-only журнал всех подсказок всех
// субъектов: фильтры статус/группа/характер, отдельные состояния «записей нет»
// и «по фильтру записей нет», карточки без кнопок мутации.
// Traceability: doc:.wf-research/ui-concept/concept.md#6-раздел-подсказки-маршрут-hints
// Перенос №15 фиксирует выделенный маршрут журнала подсказок `/hints`.
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

const hintsLogKey = (filter: HintLogFilter) => ["hints-log", filter] as const;

const STATUS_OPTIONS: ReadonlyArray<{ value: HintStatus; label: string }> = [
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
	const isMobile = useIsMobile();
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
		<section className="flex flex-col gap-4 p-6">
			<h1 className="text-2xl font-semibold tracking-tight">Подсказки</h1>

			<div className="flex flex-wrap items-center gap-2">
				<select
					aria-label="Фильтр по статусу"
					className="rounded-md border px-2 py-1 text-sm"
					value={status}
					onChange={(event) => setStatus(event.currentTarget.value as HintStatus | "")}
				>
					<option value="">статус: все</option>
					{STATUS_OPTIONS.map((option) => (
						<option key={option.value} value={option.value}>
							статус: {option.label}
						</option>
					))}
				</select>

				<select
					aria-label="Фильтр по группе"
					className="rounded-md border px-2 py-1 text-sm"
					value={groupId}
					onChange={(event) => setGroupId(event.currentTarget.value)}
				>
					<option value="">группа: все</option>
					{GROUP_OPTIONS.map((option) => (
						<option key={option.id} value={option.id}>
							группа: {option.title}
						</option>
					))}
				</select>

				<select
					aria-label="Фильтр по характеру"
					className="rounded-md border px-2 py-1 text-sm"
					value={character}
					onChange={(event) => setCharacter(event.currentTarget.value)}
				>
					<option value="">характер: все</option>
					{availableCharacters.map((value) => (
						<option key={value} value={value}>
							характер: {characterLabel(value)}
						</option>
					))}
				</select>

				{hasActiveFilter && (
					<button
						type="button"
						className="rounded-md border px-2 py-1 text-sm"
						onClick={() => {
							setStatus("");
							setGroupId("");
							setCharacter("");
						}}
					>
						Сбросить фильтры
					</button>
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

			{query.data !== undefined && query.data.items.length > 0 && isMobile == false && (
				<div className="space-y-2">
					<p className="text-muted-foreground text-sm">
						Показано {query.data.items.length} из {query.data.total}
					</p>
					<div className="rounded-md border">
						<Table>
							<TableHeader>
								<TableRow>
									<TableHead>as-of</TableHead>
									<TableHead>Субъект</TableHead>
									<TableHead>Группа</TableHead>
									<TableHead>Характер</TableHead>
									<TableHead>Статус</TableHead>
									<TableHead>Подсказка</TableHead>
									<TableHead>Источники</TableHead>
								</TableRow>
							</TableHeader>
							<TableBody>
								{query.data.items.map((item) => (
									<HintsLogRow key={item.id} item={item} />
								))}
							</TableBody>
						</Table>
					</div>
				</div>
			)}
			{query.data !== undefined && query.data.items.length > 0 && isMobile && (
				<div className="space-y-2">
					{/* На мобильном журнал подсказок показывает те же данные карточками
					    вместо широкой таблицы, чтобы сохранить читаемость без потери
					    атрибутов записи и следов источников. */}
					{/* Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив */}
					<p className="text-muted-foreground text-sm">
						Показано {query.data.items.length} из {query.data.total}
					</p>
					<ul className="flex flex-col gap-2">
						{query.data.items.map((item) => (
							<li key={item.id} className="rounded-md border p-3 text-sm">
								<p className="mb-1 font-medium">{item.text}</p>
								<p className="text-muted-foreground text-xs">{formatMoment(item.asOf)} · {statusLabel(item.status)}</p>
								<p className="text-xs">субъект: {item.subject.kind === "journal" ? "журнал" : `конструкция ${item.subject.constructionId ?? ""}`}</p>
								<p className="text-xs">группа: {item.group.title} · характер: {characterLabel(item.character)}</p>
							</li>
						))}
					</ul>
				</div>
			)}
		</section>
	);
}

function HintsLogRow({ item }: { item: HintLogRecord }) {
	return (
		<TableRow>
			<TableCell>{formatMoment(item.asOf)}</TableCell>
			<TableCell>{renderSubject(item)}</TableCell>
			<TableCell>{item.group.title}</TableCell>
			<TableCell>{characterLabel(item.character)}</TableCell>
			<TableCell>{statusLabel(item.status)}</TableCell>
			<TableCell>
				<div className="flex flex-col gap-1">
					<p className="max-w-2xl text-wrap whitespace-normal">{item.text}</p>
					<p className="text-muted-foreground text-xs">правило {item.ruleId} · {item.clarity}</p>
					{Object.keys(item.facts).length > 0 && (
						<ul className="text-muted-foreground list-disc pl-4 text-xs">
							{Object.entries(item.facts).map(([key, value]) => (
								<li key={key}>
									<span className="font-medium">{key}</span> {value}
								</li>
							))}
						</ul>
					)}
				</div>
			</TableCell>
			<TableCell>
				{item.sources.length === 0 ? (
					<span className="text-muted-foreground text-xs">—</span>
				) : (
					<ul className="flex flex-col gap-1 text-xs">
						{item.sources.map((source) => (
							<li key={`${item.id}:${source.tag}:${source.file}`} className="space-y-0.5">
								<div className="flex flex-wrap items-center gap-1">
									<span className="rounded bg-muted px-1 py-0.5 font-medium">{source.tag}</span>
									<span className="font-mono">{source.file}</span>
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
			</TableCell>
		</TableRow>
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
