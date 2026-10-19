import { Inbox, Layers, X } from "lucide-react";
import * as React from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { ManualTradeInput, ManualTradeSide } from "@/lib/api/manual-trades";
import { cn } from "@/lib/utils";

// Форма ручной сделки по мастеру «Мастер/Форма ручной сделки · состояния»
// (XJapZ) макета design.pen: карточка $surface радиуса 12 с каймой $border,
// шапка «Ручная сделка» с крестиком, строка «Попадёт» с чипом цели,
// поля «Дата и время», «Символ», ряд «Сторона × Количество», ряд
// «Цена × Комиссия» и футер с Ghost-«Отмена» и Primary-действием.
// Создание из «Входящих» помещает сделку в общий пул; из карточки — сразу
// в показанную конструкцию; валидация значений выполняется на сервере,
// человекочитаемая причина отказа показывается под формой.
// Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui
// Traceability: openspec:ui/screens#scenario-inbox-create-manual-trade
// Traceability: openspec:ui/screens#scenario-card-create-manual-trade

/** Цель создания: показанная конструкция либо общий пул «Входящих». */
export interface ManualTradeFormTarget {
	/** Имя конструкции-цели; null — сделка попадёт во «Входящие». */
	readonly constructionName: string | null;
}

/** Значения формы в строках полей ввода. */
export interface ManualTradeFormValues {
	/** Момент исполнения в формате datetime-local. */
	readonly executedAt: string;
	readonly symbol: string;
	readonly side: ManualTradeSide;
	readonly quantity: string;
	readonly price: string;
	readonly feeUsdt: string;
}

/** Пропсы формы ручной сделки: цель, начальные значения и команды. */
export interface ManualTradeFormProps {
	/** Цель создания, отображаемая чипом строки «Попадёт». */
	readonly target: ManualTradeFormTarget;
	/** Начальные значения полей; пустая форма для создания подставляет текущий момент. */
	readonly initialValues?: ManualTradeFormValues | null;
	/** Подпись основного действия: «Создать» при внесении, «Сохранить» при правке. */
	readonly submitLabel: string;
	/** Состояние отправки — блокирует действия формы. */
	readonly pending: boolean;
	/** Человекочитаемая причина отказа серверной валидации. */
	readonly error: string | null;
	/** Отправка значений формы; некорректные числа форму не покидают. */
	readonly onSubmit: (input: ManualTradeInput) => void;
	/** Закрытие формы без отправки. */
	readonly onCancel: () => void;
}

/** Значение datetime-local для текущего момента: дата подставлена, число вводится. */
function nowLocalInputValue(): string {
	return toLocalInputValue(new Date().toISOString());
}

/** Переводит ISO-момент в значение поля datetime-local в зоне браузера. */
function toLocalInputValue(iso: string): string {
	const date = new Date(iso);
	const pad = (value: number) => String(value).padStart(2, "0");
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Число из строки поля: запятая — десятичный разделитель; null при пустом вводе. */
function parseNumberField(value: string): number | null {
	const trimmed = value.trim().replace(",", ".");
	if (trimmed.length === 0) {
		return null;
	}

	const parsed = Number(trimmed);
	return Number.isFinite(parsed) ? parsed : null;
}

export function ManualTradeForm({ target, initialValues, submitLabel, pending, error, onSubmit, onCancel }: ManualTradeFormProps) {
	const [values, setValues] = React.useState<ManualTradeFormValues>(() => ({
		executedAt: initialValues?.executedAt ?? nowLocalInputValue(),
		symbol: initialValues?.symbol ?? "",
		side: initialValues?.side ?? "Buy",
		quantity: initialValues?.quantity ?? "",
		price: initialValues?.price ?? "",
		feeUsdt: initialValues?.feeUsdt ?? "",
	}));
	const [clientError, setClientError] = React.useState<string | null>(null);

	const setField = (patch: Partial<ManualTradeFormValues>) => {
		setValues((current) => ({ ...current, ...patch }));
	};

	// Значения формы отправляются на серверную валидацию без клиентских
	// ограничений правил (дата, количества и комиссия проверяются сервером);
	// непарсируемые числа остаются форме с пояснением — JSON их не переносит.
	// Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui
	const submit = () => {
		const executedAt = new Date(values.executedAt);
		const quantity = parseNumberField(values.quantity);
		const price = parseNumberField(values.price);
		const feeUsdt = parseNumberField(values.feeUsdt);
		if (Number.isNaN(executedAt.getTime()) || values.quantity.trim().length > 0 && quantity === null
			|| values.price.trim().length > 0 && price === null || values.feeUsdt.trim().length > 0 && feeUsdt === null) {
			setClientError("Введите корректные дату и числовые значения полей.");
			return;
		}

		setClientError(null);
		onSubmit({
			executedAt: executedAt.toISOString(),
			symbol: values.symbol.trim(),
			side: values.side,
			quantity: quantity ?? 0,
			price: price ?? 0,
			feeUsdt,
		});
	};

	return (
		// Карточка формы по мастер-ноде формы (XJapZ): $surface, радиус 12,
		// кайма $border, паддинг 16, вертикальный зазор 10.
		// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
		<section data-slot="manual-trade-form" className="flex flex-col gap-2.5 rounded-lg border bg-card p-4">
			<div className="flex items-center gap-2">
				<h3 className="text-[14px] font-semibold text-foreground">Ручная сделка</h3>
				<span className="h-px flex-1" aria-hidden="true" />
				<Button
					variant="ghost"
					size="icon"
					className="size-6"
					aria-label="Закрыть форму ручной сделки"
					disabled={pending}
					onClick={onCancel}
				>
					<X aria-hidden className="size-3.5 text-text-muted" />
				</Button>
			</div>

			{/* Строка «Попадёт»: чип цели — пилюля $surface с каймой $border;
			    у «Входящих» иконка inbox в $info, у конструкции — layers в
			    $accentStrong (макет fYadZ). */}
			<div className="flex items-center gap-1.5">
				<span className="text-[11px] text-text-muted">Попадёт</span>
				<span className="inline-flex items-center gap-1.5 rounded-full border bg-surface px-2.5 py-[3px] text-[11.5px] text-text-secondary">
					{target.constructionName === null ? (
						<Inbox aria-hidden className="size-3 text-info" />
					) : (
						<Layers aria-hidden className="size-3 text-accent-strong" />
					)}
					{target.constructionName ?? "Во «Входящие»"}
				</span>
			</div>

			<div className="flex flex-col gap-1">
				<span className="text-[11px] font-medium text-text-muted">Дата и время</span>
				<Input
					aria-label="Дата и время сделки"
					type="datetime-local"
					value={values.executedAt}
					onChange={(event) => setField({ executedAt: event.currentTarget.value })}
				/>
			</div>

			<div className="flex flex-col gap-1">
				<span className="text-[11px] font-medium text-text-muted">Символ</span>
				<Input
					aria-label="Символ инструмента"
					placeholder="BTCUSDT"
					value={values.symbol}
					onChange={(event) => setField({ symbol: event.currentTarget.value })}
				/>
			</div>

			<div className="flex gap-2.5">
				<div className="flex min-w-0 flex-1 flex-col gap-1">
					<span className="text-[11px] font-medium text-text-muted">Сторона</span>
					{/* Сегмент стороны по макету: активная — $accentSoft/$accentStrong,
					    неактивная — кайма $border и $textSecondary. */}
					<div className="flex gap-1.5" role="radiogroup" aria-label="Сторона сделки">
						{(["Buy", "Sell"] as const).map((side) => {
							const active = values.side === side;
							return (
								<button
									key={side}
									type="button"
									role="radio"
									aria-checked={active}
									disabled={pending}
									onClick={() => setField({ side })}
									className={cn(
										"rounded-[8px] px-3.5 py-[6px] text-[12.5px] font-medium transition-colors",
										active ? "bg-accent-soft text-accent-strong" : "border text-text-secondary",
									)}
								>
									{side === "Buy" ? "Покупка" : "Продажа"}
								</button>
							);
						})}
					</div>
				</div>
				<label className="flex min-w-0 flex-1 flex-col gap-1">
					<span className="text-[11px] font-medium text-text-muted">Количество</span>
					<Input
						inputMode="decimal"
						value={values.quantity}
						onChange={(event) => setField({ quantity: event.currentTarget.value })}
					/>
				</label>
			</div>

			<div className="flex gap-2.5">
				<label className="flex min-w-0 flex-1 flex-col gap-1">
					<span className="text-[11px] font-medium text-text-muted">Цена, USDT</span>
					<Input
						inputMode="decimal"
						value={values.price}
						onChange={(event) => setField({ price: event.currentTarget.value })}
					/>
				</label>
				<label className="flex min-w-0 flex-1 flex-col gap-1">
					<span className="text-[11px] font-medium text-text-muted">Комиссия, USDT</span>
					<Input
						inputMode="decimal"
						value={values.feeUsdt}
						onChange={(event) => setField({ feeUsdt: event.currentTarget.value })}
					/>
				</label>
			</div>

			{/* Футер мастера: Ghost-«Отмена» слева от Primary-действия, справа. */}
			<div className="flex items-center gap-2 pt-0.5">
				<span className="h-px flex-1" aria-hidden="true" />
				<Button variant="ghost" size="sm" disabled={pending} onClick={onCancel}>
					Отмена
				</Button>
				<Button size="sm" disabled={pending || values.symbol.trim().length === 0} onClick={submit}>
					{submitLabel}
				</Button>
			</div>

			{/* Причина отказа серверной валидации либо локального разбора чисел. */}
			{(clientError ?? error) !== null && (
				<p className="text-destructive text-sm" role="alert">
					{clientError ?? error}
				</p>
			)}
		</section>
	);
}
