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

// Вход кейса C3: граница реального риска прижата к левому краю
// (−550 = −250 − 300), маркер — итог −70, подписи «риск есть /
// 300 USDT · 100%» и «−70».
const CASE_C3 = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: -250,
	unrealized: 180,
	quotesDegraded: false,
	hasOpenResidual: true,
} as const;

// Вход кейса C6: сверхприбыль — итог +1 400 растягивает золотую зону,
// маркер стоит на краю шкалы и подписан итогом.
const CASE_C6 = {
	plannedRisk: 300,
	plannedProfit: 900,
	realized: 150,
	unrealized: 1250,
	quotesDegraded: false,
	hasOpenResidual: true,
} as const;

describe("полный индикатор", () => {
	// Граница прижата к левому краю, но подписывается реальным риском целиком;
	// маркер подписан итогом.
	// Traceability: openspec:ui/screens#scenario-finresult-border-left-clip
	it("показывает подписи шкалы, границы и маркера кейса C3", () => {
		// Act: полный вид для карточки и превью конструкции.
		render(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: деления шкалы «−300 / 0 / +900» и подписи кейса.
		expect(screen.getByText("−300")).toBeInTheDocument();
		expect(screen.getByText("0")).toBeInTheDocument();
		expect(screen.getByText("+900")).toBeInTheDocument();
		expect(screen.getByText("риск есть")).toBeInTheDocument();
		expect(screen.getByText("300 USDT · 100%")).toBeInTheDocument();
		expect(screen.getByText("−70")).toBeInTheDocument();
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

	// Сверхприбыль: золотая зона тянется до итога, маркер остаётся на краю
	// шкалы без клипа и подписан итогом.
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
	it("рисует золотую зону до итога и подписывает маркер итогом", () => {
		// Act: итог +1 400 на краю растянутой шкалы (кейс C6).
		const { container } = render(<FullFinResultIndicator input={CASE_C6} />);

		// Assert: золотая зона отрисована, маркер подписан итогом
		// (тысячный разделитель — неразрывный пробел, при поиске текст
		// нормализуется в обычный).
		expect(container.querySelector('[data-part="super-zone"]')).not.toBeNull();
		expect(screen.getByText("+1 400")).toBeInTheDocument();
	});

	// Свежая позиция: ликвидация вернёт около нуля, весь реальный риск (120)
	// впереди — граница стоит на −реальныйРиск со своей метрикой.
	// Traceability: openspec:ui/screens#scenario-finresult-fresh-position-real-risk
	it("показывает свежую позицию: маркер «≈ 0», граница по метрике риска", () => {
		// Act: сделок закрытия нет, метрика реального риска доступна.
		render(
			<FullFinResultIndicator
				input={{
					plannedRisk: 300,
					plannedProfit: 900,
					realized: 0,
					unrealized: 0,
					quotesDegraded: false,
					hasOpenResidual: true,
					realRisk: 120,
				}}
			/>,
		);

		// Assert: маркер «≈ 0», граница подписана метрикой реального риска.
		expect(screen.getByText("≈ 0")).toBeInTheDocument();
		expect(screen.getByText("риск есть")).toBeInTheDocument();
		expect(screen.getByText("120 USDT · 40%")).toBeInTheDocument();
	});

	// Закрытая конструкция: реального риска нет — граница на realized с
	// подписью «риска нет» без процентов.
	// Traceability: openspec:ui/screens#scenario-finresult-closed-no-risk
	it("показывает закрытую позицию: «риска нет», граница на realized", () => {
		// Act: остатков нет, реализованная выше целевого профита, риск 0.
		render(
			<FullFinResultIndicator
				input={{
					plannedRisk: 300,
					plannedProfit: 900,
					realized: 945,
					unrealized: null,
					quotesDegraded: false,
					hasOpenResidual: false,
					realRisk: 0,
				}}
			/>,
		);

		// Assert: подпись «риска нет» и итог +945 на краю шкалы.
		expect(screen.getByText("риска нет")).toBeInTheDocument();
		expect(screen.getByText("0 USDT · 0%")).toBeInTheDocument();
		expect(screen.getByText("+945")).toBeInTheDocument();
	});

	// Нет метрики реального риска — граница считается по заглушке плановым
	// риском, подпись показывает величину заглушки.
	// Traceability: openspec:ui/screens#scenario-finresult-real-risk-fallback-planned
	it("показывает заглушку границы плановым риском, когда метрики нет", () => {
		// Act: поле realRisk не передано вызывающим компонентом.
		render(
			<FullFinResultIndicator
				input={{
					plannedRisk: 300,
					plannedProfit: 900,
					realized: 100,
					unrealized: 0,
					quotesDegraded: false,
					hasOpenResidual: true,
				}}
			/>,
		);

		// Assert: граница по плановому риску 300, маркер — итог +100.
		expect(screen.getByText("риск есть")).toBeInTheDocument();
		expect(screen.getByText("300 USDT · 100%")).toBeInTheDocument();
		expect(screen.getByText("+100")).toBeInTheDocument();
	});
});

describe("средний индикатор", () => {
	it("показывает шкалу и подписи в уменьшенном виде", () => {
		// Act: средний вид для сводных карточек.
		render(<MediumFinResultIndicator input={CASE_C3} />);

		// Assert: те же смысловые подписи, что и в полном виде; маркер — итог.
		expect(screen.getByText("−300")).toBeInTheDocument();
		expect(screen.getByText("риск есть")).toBeInTheDocument();
		expect(screen.getByText("−70")).toBeInTheDocument();
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
				input={{ plannedRisk: null, plannedProfit: null, realized: -50, unrealized: 30, quotesDegraded: false, hasOpenResidual: true }}
			/>,
		);

		// Assert: ни зон, ни границы — заполнение от нуля до края.
		expect(container.querySelector('[data-part="risk-zone"]')).toBeNull();
		expect(container.querySelector('[data-part="profit-zone"]')).toBeNull();
		expect(container.querySelector('[data-part="border"]')).toBeNull();
		expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
	});

	it("помечает сбой котировок признаком неполноты", () => {
		// Act: сбой котировок при открытых остатках — комбинация, которую
		// отдают read-модели: нереализованная часть не оценена, но остатки
		// открыты, поэтому скрывать есть что.
		// Traceability: openspec:ui/screens#scenario-finresult-marks-failure-partial
		const { container } = render(
			<CompactFinResultIndicator
				input={{ plannedRisk: 300, plannedProfit: 900, realized: -250, unrealized: null, quotesDegraded: true, hasOpenResidual: true }}
			/>,
		);

		// Assert: индикатор помечен неполным; единая заливка строится по
		// доступной части итога — состав заливки проверяют эталоны калькулятора.
		const root = container.firstElementChild as HTMLElement;
		expect(root.dataset.incomplete).toBe("true");
		expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
	});

	// Строки подписи границы рендерятся столбцом: модель разноса считает
	// ширину по самой длинной строке (rows=2), инлайн-рендер давал одну
	// строку шире модели — маркер наплывал на границу на узких видах.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
	it("рендерит две строки подписи границы столбцом", () => {
		// Act.
		render(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: контейнер границы — flex-колонка из двух строк.
		const borderLabel = screen.getByText("риск есть").parentElement;
		expect(borderLabel).toHaveAttribute("data-part", "border-label");
		expect(borderLabel).toHaveClass("flex", "flex-col");
		expect(borderLabel?.children).toHaveLength(2);
	});

	// Перенесённая калькулятором на другую сторону метка не теряется: стек
	// собирается по фактической стороне из разноса, а не по канонической.
	// Traceability: openspec:ui/screens#scenario-finresult-labels-no-overlap
	it("рендерит метку границы в стеке фактической стороны", () => {
		// Act: итог +420 и граница +100 на одной позиции — нижний уровень
		// занят маркером, граница уходит наверх.
		const { container } = render(
			<FullFinResultIndicator
				input={{ plannedRisk: 300, plannedProfit: 900, realized: 520, unrealized: -100, quotesDegraded: false, hasOpenResidual: true, realRisk: 100 }}
			/>,
		);

		// Assert: подпись границы живёт в стеке над полосой (первый ребёнок
		// корня), а не в стеке под полосой.
		const borderLabel = screen.getByText("риск есть").parentElement;
		expect(borderLabel).not.toBeNull();
		const root = container.firstElementChild as HTMLElement;
		expect(borderLabel?.parentElement).toBe(root.firstElementChild);
	});
});

// Единая заливка итога после упрощения рендера: отдельного слоя
// нереализованной части больше нет, а зелёный и золотой участки
// положительного итога стыкуются без перекрытия. Контракт общий, но
// проверяется на каждом из трёх представлений.
describe("заливки итога во всех представлениях", () => {
	const views = [FullFinResultIndicator, MediumFinResultIndicator, CompactFinResultIndicator];

	// fill-unreal удалён из рендера: слой нереализованной части не рисуется
	// ни в одном представлении ни при отрицательном итоге, ни при
	// сверхприбыли — заливка итога всегда одна (fill-main).
	// Traceability: openspec:ui/screens#scenario-finresult-negative-total-single-fill
	it("не рисует fill-unreal ни в одном представлении", () => {
		// Arrange: отрицательный итог (кейс C3) и сверхприбыль (кейс C6) —
		// раньше оба рисовали fill-unreal при ненулевой нереализованной части.
		const cases = [CASE_C3, CASE_C6];

		for (const View of views) {
			for (const input of cases) {
				// Act: рендер каждого представления на каждом знаке итога.
				const { container, unmount } = render(<View input={input} />);

				// Assert: fill-unreal отсутствует, единая заливка на месте.
				expect(container.querySelector('[data-part="fill-unreal"]')).toBeNull();
				expect(container.querySelector('[data-part="fill-main"]')).not.toBeNull();
				unmount();
			}
		}
	});

	// Золотой участок видим: зелёная заливка заканчивается ровно на плановом
	// профите, золотая тянется от профита до маркера на краю шкалы — слои
	// стыкуются без перекрытия, видимость золота не зависит от их порядка.
	// Traceability: openspec:ui/screens#scenario-finresult-super-zone-marker
	it("рисует зелёный и золотой участки встык до маркера", () => {
		for (const View of views) {
			// Act: кейс C6 — профит +900, итог +1 400 на краю растянутой шкалы
			// (шкала −300…+1 400: ноль 300/1700, профит 1200/1700).
			const { container, unmount } = render(<View input={CASE_C6} />);
			const fill = container.querySelector<HTMLElement>('[data-part="fill-main"]');
			const superZone = container.querySelector<HTMLElement>('[data-part="super-zone"]');
			const marker = container.querySelector<HTMLElement>('[data-part="marker"]');

			// Assert: зелёный участок от нуля до планового профита.
			expect(fill).not.toBeNull();
			expect(Number.parseFloat(fill?.style.left ?? "")).toBeCloseTo((300 / 1700) * 100, 3);
			expect(
				Number.parseFloat(fill?.style.left ?? "") + Number.parseFloat(fill?.style.width ?? ""),
			).toBeCloseTo((1200 / 1700) * 100, 3);
			// Assert: золотой участок от планового профита до маркера итога.
			expect(superZone).not.toBeNull();
			expect(Number.parseFloat(superZone?.style.left ?? "")).toBeCloseTo((1200 / 1700) * 100, 3);
			expect(
				Number.parseFloat(superZone?.style.left ?? "") +
					Number.parseFloat(superZone?.style.width ?? ""),
			).toBeCloseTo(Number.parseFloat(marker?.style.left ?? ""), 3);
			unmount();
		}
	});
});

describe("доступность индикатора", () => {
	it("несёт текстовое описание состояния для скринридеров", () => {
		// Act.
		render(<FullFinResultIndicator input={CASE_C3} />);

		// Assert: состояние озвучивается подписями границы и итога маркера.
		const labelled = screen.getByRole("img", { name: /риск есть 300 USDT · 100%/ });
		expect(labelled).toHaveAttribute("aria-label", expect.stringContaining("−70"));
	});
});
