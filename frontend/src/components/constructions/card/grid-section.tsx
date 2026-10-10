import * as React from "react";

// Секция-сетка карточки конструкции по фрейму «Экран/Карточка конструкции»
// design.pen (QbFRE): белая карточка радиусом 12 (--radius-lg) с бордером
// border #E5E5DF; в головке секции — заголовок Inter 13/600, счётчик
// «N колонок · M» Inter 11.5 приглушённым текстом и дополнительный контент
// (например, чип предупреждений закрывающих записей).
// Traceability: change:rework-spa-shell-and-screens/specs/ui/screens/spec#scenario-detail-grids-match-design
// Traceability: openspec:ui/screens#requirement-construction-detail-screen

/** Пропсы секции-сетки: заголовок, число колонок и строк, доп. контент головки. */
export interface GridSectionProps {
	/** Заголовок секции («Позиции», «Сделки», …). */
	readonly title: string;
	/** Число колонок таблицы — для счётчика головки. */
	readonly columnCount: number;
	/** Число строк данных — для счётчика головки. */
	readonly rowCount: number;
	/** Дополнительный контент головки после счётчика (чип предупреждений). */
	readonly headExtra?: React.ReactNode;
	/** data-slot для тестов и якорей. */
	readonly dataSlot?: string;
	readonly children: React.ReactNode;
}

/** Склонение слова «колонка» для счётчика: 1 колонка, 2 колонки, 5 колонок. */
export function columnsLabel(count: number): string {
	const mod10 = count % 10;
	const mod100 = count % 100;
	if (mod10 === 1 && mod100 !== 11) {
		return "колонка";
	}

	if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
		return "колонки";
	}

	return "колонок";
}

export function GridSection({ title, columnCount, rowCount, headExtra, dataSlot, children }: GridSectionProps) {
	return (
		<section data-slot={dataSlot} className="bg-card flex flex-col overflow-hidden rounded-lg border">
			{/* Головка секции: паддинг [10,14], зазор 8; дети идут под ней без обёртки. */}
			<div className="flex items-center gap-2 px-3.5 py-2.5">
				<h2 className="text-[13px] font-semibold text-foreground">{title}</h2>
				<span className="text-[11.5px] text-text-muted">
					{columnCount} {columnsLabel(columnCount)} · {rowCount}
				</span>
				{headExtra}
			</div>
			{children}
		</section>
	);
}
