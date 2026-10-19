import { X } from "lucide-react";
import * as React from "react";
import type { ConstructionCardTrade, SyntheticCloseManualLinkInput } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

// Форма ручной синтетической связки по мастеру «Мастер/Синтетика · форма
// ручной связки» (vG1f8) макета design.pen: карточка $surface радиуса 12
// с каймой $border, шапка «Ручная синтетическая связка» с крестиком, поля
// «Линейная сделка» и «Встречный опцион» (выбор из сделок конструкции,
// подсказка про страйк и экспирацию) и «Количество» (не больше каждой
// из сделок), футер с Ghost-«Отмена» и Primary-«Создать связку».
// Правила пары (одна конструкция, совпадение страйка и экспирации,
// согласованное количество, без окна времени) проверяет сервер — форма
// показывает человекочитаемую причину отказа.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link

/** Значения формы в строках полей ввода. */
export interface ManualSyntheticLinkFormValues {
	/** Идентификатор выбранной линейной сделки; пустая строка — не выбрана. */
	readonly linearTradeId: string;
	/** Идентификатор выбранного встречного опциона; пустая строка — не выбран. */
	readonly optionTradeId: string;
	/** Количество связки в строке поля ввода. */
	readonly quantity: string;
}

/** Пропсы формы ручной синтетической связки. */
export interface ManualSyntheticLinkFormProps {
	/** Сделки конструкции — кандидаты обеих сторон связки. */
	readonly trades: readonly ConstructionCardTrade[];
	/** Состояние отправки — блокирует действия формы. */
	readonly pending: boolean;
	/** Человекочитаемая причина отказа серверной валидации. */
	readonly error: string | null;
	/** Отправка значений формы; некорректное количество форму не покидает. */
	readonly onSubmit: (input: SyntheticCloseManualLinkInput) => void;
	/** Закрытие формы без отправки. */
	readonly onCancel: () => void;
}

/** Подпись сделки в выборе: инструмент · сторона и количество · цена. */
function tradeOptionLabel(trade: ConstructionCardTrade): string {
	return `${trade.symbol} · ${trade.isBuy ? "покупка" : "продажа"} ${trade.quantity} · ${trade.price}`;
}

/** Единый ключ сделки связки: execId биржевой, ключ сырья ручной. */
function tradeKey(trade: ConstructionCardTrade): string {
	return trade.execId ?? `manual:${trade.manualTradeId}`;
}

/** Число из строки поля: запятая — десятичный разделитель; null при пустом или некорректном вводе. */
function parseQuantity(value: string): number | null {
	const trimmed = value.trim().replace(",", ".");
	if (trimmed.length === 0) {
		return null;
	}

	const parsed = Number(trimmed);
	return Number.isFinite(parsed) ? parsed : null;
}

export function ManualSyntheticLinkForm({ trades, pending, error, onSubmit, onCancel }: ManualSyntheticLinkFormProps) {
	const [values, setValues] = React.useState<ManualSyntheticLinkFormValues>({
		linearTradeId: "",
		optionTradeId: "",
		quantity: "",
	});
	const [clientError, setClientError] = React.useState<string | null>(null);

	const setField = (patch: Partial<ManualSyntheticLinkFormValues>) => {
		setValues((current) => ({ ...current, ...patch }));
	};

	// Количество связки проверяет сервер (больше нуля, не больше каждой из
	// сделок); непарсируемое число остаётся форме с пояснением — JSON его
	// не переносит.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	const submit = () => {
		const quantity = parseQuantity(values.quantity);
		if (quantity === null) {
			setClientError("Введите количество связки числом.");
			return;
		}

		setClientError(null);
		onSubmit({
			linearTradeId: values.linearTradeId,
			optionTradeId: values.optionTradeId,
			quantity,
		});
	};

	// Подпись-пояснение мастер показывает в заполненном состоянии формы:
	// она объясняет правило связки, когда обе сделки и количество выбраны.
	const captionVisible = values.linearTradeId !== "" && values.optionTradeId !== "" && values.quantity.trim().length > 0;

	return (
		// Карточка формы по мастер-ноде vG1f8: $surface, радиус 12, кайма
		// $border, паддинг 16, вертикальный зазор 10.
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		<section data-slot="manual-synthetic-link-form" className="flex flex-col gap-2.5 rounded-lg border bg-card p-4">
			<div className="flex items-center gap-2">
				<h3 className="text-[14px] font-semibold text-foreground">Ручная синтетическая связка</h3>
				<span className="h-px flex-1" aria-hidden="true" />
				<Button
					variant="ghost"
					size="icon"
					className="size-6"
					aria-label="Закрыть форму ручной синтетической связки"
					disabled={pending}
					onClick={onCancel}
				>
					<X aria-hidden className="size-3.5 text-text-muted" />
				</Button>
			</div>

			<div className="flex flex-col gap-1">
				<span className="text-[11px] font-medium text-text-muted">Линейная сделка</span>
				<select
					aria-label="Линейная сделка связки"
					className="h-9 rounded-sm border bg-card px-2.5 text-xs text-foreground outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
					value={values.linearTradeId}
					onChange={(event) => setField({ linearTradeId: event.currentTarget.value })}
				>
					<option value="">Выберите линейную сделку…</option>
					{trades.map((trade) => (
						<option key={tradeKey(trade)} value={tradeKey(trade)}>
							{tradeOptionLabel(trade)}
						</option>
					))}
				</select>
			</div>

			<div className="flex flex-col gap-1">
				<span className="text-[11px] font-medium text-text-muted">Встречный опцион</span>
				<select
					aria-label="Встречный опцион связки"
					className="h-9 rounded-sm border bg-card px-2.5 text-xs text-foreground outline-none focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50"
					value={values.optionTradeId}
					onChange={(event) => setField({ optionTradeId: event.currentTarget.value })}
				>
					<option value="">Выберите встречный опцион…</option>
					{trades.map((trade) => (
						<option key={tradeKey(trade)} value={tradeKey(trade)}>
							{tradeOptionLabel(trade)}
						</option>
					))}
				</select>
				{/* Подсказка правила пары из мастера: страйк и экспирация
				    встречного опциона обязаны совпадать с закрываемой ногой. */}
				<span className="text-[10.5px] text-text-muted">страйк и экспирация должны совпадать с закрываемой ногой</span>
			</div>

			<label className="flex flex-col gap-1">
				<span className="text-[11px] font-medium text-text-muted">Количество</span>
				<Input
					aria-label="Количество связки"
					inputMode="decimal"
					placeholder="Не больше каждой из сделок"
					value={values.quantity}
					onChange={(event) => setField({ quantity: event.currentTarget.value })}
				/>
			</label>

			{/* Футер мастера: Ghost-«Отмена» слева от Primary-«Создать связку»,
			    справа; действие недоступно, пока сделки не выбраны. */}
			<div className="flex items-center gap-2 pt-0.5">
				<span className="h-px flex-1" aria-hidden="true" />
				<Button variant="ghost" size="sm" disabled={pending} onClick={onCancel}>
					Отмена
				</Button>
				<Button
					size="sm"
					disabled={pending || values.linearTradeId === "" || values.optionTradeId === ""}
					onClick={submit}
				>
					Создать связку
				</Button>
			</div>

			{/* Пояснение правила связки — подпись заполненного состояния мастера. */}
			{captionVisible && (
				<p className="text-[10.5px] text-text-muted">
					Связка фиксирует паритетный лок: линейная сделка + встречный опцион по совпадающим страйку и экспирации;
					количество не превышает ни одну из сделок.
				</p>
			)}

			{/* Причина отказа серверной валидации либо локального разбора количества. */}
			{(clientError ?? error) !== null && (
				<p className="text-destructive text-sm" role="alert">
					{clientError ?? error}
				</p>
			)}
		</section>
	);
}
