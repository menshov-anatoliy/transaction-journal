import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
	CompactFinResultIndicator,
	FullFinResultIndicator,
	MediumFinResultIndicator,
} from "./fin-result-indicator";

// Смоук-проверки компонентов индикатора финрезультата: рендер строится на
// геометрии калькулятора (юнит-эталоны C1–C7 покрыты в geometry.test.ts),
// здесь проверяем публичный контракт представлений — какие подписи, части
// и доступность выдаёт каждый из трёх видов.
// Traceability: doc:.wf-research/ui-concept/concept.md#9-индикатор-финансового-результата-конструкции

// Вход кейса C3 макета: граница −250, маркер −70, подписи «риск есть /
// 250 USDT · 83%» и «+180».
const CASE_C3 = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: -250,
	unrealized: 180,
	quotesDegraded: false,
} as const;

// Вход кейса C6 макета: сверхприбыль с золотой зоной и клипом маркера.
const CASE_C6 = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: 150,
	unrealized: 1250,
	quotesDegraded: false,
} as const;

describe("полный индикатор", () => {
	it("показывает подписи шкалы, границы и маркера кейса C3", () => {
		// Act: полный вид для карточки и превью конструкции.
		render(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: деления шкалы «−300 / 0 / +900» и подписи кейса.
		expect(screen.getByText("−300")).toBeInTheDocument();
		expect(screen.getByText("0")).toBeInTheDocument();
		expect(screen.getByText("+900")).toBeInTheDocument();
		expect(screen.getByText("риск есть")).toBeInTheDocument();
		expect(screen.getByText("250 USDT · 83%")).toBeInTheDocument();
		expect(screen.getByText("+180")).toBeInTheDocument();
	});

	it("позиционирует маркер по доле геометрии", () => {
		// Act.
		const { container } = render(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: маркер стоит на доле итога −70 из шкалы −300…+900.
		const marker = container.querySelector<HTMLElement>('[data-part="marker"]');
		expect(marker).not.toBeNull();
		expect(Number.parseFloat(marker?.style.left ?? "")).toBeCloseTo((230 / 1200) * 100, 3);
	});

	it("помечает тон маркера по знаку итога", () => {
		// Act: итог отрицательный (кейс C3).
		const { container } = render(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: маркер и подписи в отрицательном тоне.
		const marker = container.querySelector<HTMLElement>('[data-part="marker"]');
		expect(marker?.dataset.tone).toBe("negative");
	});

	it("показывает признак пробоя: вынос значения при клипе маркера", () => {
		// Act: итог +1 400 за границей шкалы (кейс C6).
		render(<FullFinResultIndicator input={CASE_C6} />);

		// Assert: значение итога вынесено числом, край золота подписан
		// ( Thousands-разделитель — неразрывный пробел, при поиске текст
		// нормализуется в обычный).
		expect(screen.getByText("+1 400")).toBeInTheDocument();
		expect(screen.getByText("+1 150")).toBeInTheDocument();
	});
});

describe("средний индикатор", () => {
	it("показывает шкалу и подписи в уменьшенном виде", () => {
		// Act: средний вид для сводных карточек.
		render(<MediumFinResultIndicator input={CASE_C3} />);

		// Assert: те же смысловые подписи, что и в полном виде.
		expect(screen.getByText("−300")).toBeInTheDocument();
		expect(screen.getByText("риск есть")).toBeInTheDocument();
		expect(screen.getByText("+180")).toBeInTheDocument();
	});
});

describe("компактный индикатор", () => {
	// Компакт сохраняет ширину 132px мастера и токенную насечку в любой ячейке.
	// Traceability: openspec:ui/design-system#requirement-design-pen-single-source
	// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
	it("использует ширину макета и токенные цвета насечки и обводки", () => {
		// Arrange / Act: компактный индикатор с отрицательным итогом.
		const { container } = render(<CompactFinResultIndicator input={CASE_C3} />);

		// Assert: размер задаётся компонентом, цвета — дизайн-токенами.
		expect(container.firstElementChild).toHaveClass("w-[132px]");
		expect(container.querySelector('[data-part="border"]')).toHaveClass("bg-text-primary");
		expect(container.querySelector('[data-part="marker"]')).toHaveClass("border-surface");
	});

	it("рисует только полосу без текстовых подписей", () => {
		// Act: компакт для строк таблицы конструкций.
		const { container } = render(<CompactFinResultIndicator input={CASE_C3} />);

		// Assert: полоса с зонами и маркером, текстовых подписей нет.
		expect(container.querySelector('[data-part="bar"]')).not.toBeNull();
		expect(container.querySelector('[data-part="marker"]')).not.toBeNull();
		expect(container.querySelector('[data-part="border"]')).not.toBeNull();
		expect(container.textContent).toBe("");
	});

	it("нейтральная полоса не рисует зон", () => {
		// Act: плановые границы не заданы.
		const { container } = render(
			<CompactFinResultIndicator
				input={{ plannedRisk: null, plannedProfit: null, realized: -50, unrealized: 30, quotesDegraded: false }}
			/>,
		);

		// Assert: ни зон, ни границы — заполнение от нуля до края.
		expect(container.querySelector('[data-part="risk-zone"]')).toBeNull();
		expect(container.querySelector('[data-part="profit-zone"]')).toBeNull();
		expect(container.querySelector('[data-part="border"]')).toBeNull();
		expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
	});

	it("помечает сбой котировок признаком неполноты", () => {
		// Act: котировки недоступны при открытых остатках.
		const { container } = render(
			<CompactFinResultIndicator
				input={{ plannedRisk: 300, plannedProfit: 900, realized: -250, unrealized: 180, quotesDegraded: true }}
			/>,
		);

		// Assert: индикатор помечен неполным, нереализованная часть скрыта.
		const root = container.firstElementChild as HTMLElement;
		expect(root.dataset.incomplete).toBe("true");
		expect(container.querySelector('[data-part="fill-unreal"]')).toBeNull();
	});
});

describe("доступность индикатора", () => {
	it("несёт текстовое описание состояния для скринридеров", () => {
		// Act.
		render(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: состояние озвучивается подписями границы и маркера.
		const labelled = screen.getByRole("img", { name: /риск есть 250 USDT · 83%/ });
		expect(labelled).toHaveAttribute("aria-label", expect.stringContaining("+180"));
	});
});
