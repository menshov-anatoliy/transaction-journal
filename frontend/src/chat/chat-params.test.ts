import { describe, expect, it } from "vitest";
import {
	CHAT_DATA_SOURCES,
	chatParamsFromForm,
	defaultChatParams,
} from "./chat-params";
import type { ChatDataSource } from "./types";

// Параметры чата — модель с дефолтом GLM-5.3, опциональная конструкция
// и закрытый справочник источников из трёх категорий; чат создаётся
// первым сообщением с этим набором.
// Traceability: change:add-agent-chat/proposal#what-changes

describe("справочник источников данных чата", () => {
	it("содержит ровно три категории — журнал, корпус правил, рынок Bybit", () => {
		// Arrange: справочник источников — закрытый, расширяется только
		// через изменение change add-agent-chat.

		// Act: читаем идентификаторы категорий справочника.
		const ids = CHAT_DATA_SOURCES.map((source) => source.id);

		// Assert: ровно три категории и ничего сверх; у каждой есть
		// человекочитаемая подпись для формы нового чата.
		expect(ids).toEqual(["journal", "rules-corpus", "market"]);
		for (const source of CHAT_DATA_SOURCES) {
			expect(source.label.length).toBeGreaterThan(0);
			expect(source.hint.length).toBeGreaterThan(0);
		}
	});
});

describe("пресеты параметров нового чата", () => {
	it("дефолт — модель GLM-5.3, все три источника, без привязки к конструкции", () => {
		// Arrange: владелец открывает форму нового чата, ничего не выбирая.

		// Act: берём дефолтный набор параметров.
		const params = defaultChatParams();

		// Assert: дефолт модели GLM-5.3, источники — весь справочник,
		// привязки к конструкции нет — портфельный уровень журнала.
		expect(params.model).toBe("GLM-5.3");
		expect(params.constructionId).toBeNull();
		expect(params.sources).toEqual(["journal", "rules-corpus", "market"]);
	});

	it("выбор формы дополняет дефолт: модель, конструкция и подмножество источников", () => {
		// Arrange: в форме выбраны модель, конструкция и только корпус правил.
		const input = {
			model: "GLM-5.4",
			constructionId: "constr-12",
			sources: ["rules-corpus"] as const,
		};

		// Act: форма превращает выбор в параметры чата.
		const params = chatParamsFromForm(input);

		// Assert: выбранное фиксируется на создании чата как есть.
		expect(params).toEqual({
			model: "GLM-5.4",
			constructionId: "constr-12",
			sources: ["rules-corpus"],
		});
	});

	it("частичный выбор формы наследует остальное из дефолта", () => {
		// Arrange: владелец выбрал только конструкцию — модель и источники
		// не трогал.

		// Act: параметры собираются из частичного выбора.
		const params = chatParamsFromForm({ constructionId: "constr-3" });

		// Assert: модель и источники дефолтные, привязка проставлена.
		expect(params.model).toBe("GLM-5.3");
		expect(params.sources).toEqual(["journal", "rules-corpus", "market"]);
		expect(params.constructionId).toBe("constr-3");
	});

	it("пустой набор источников откатывается к полному справочнику", () => {
		// Arrange: чат без источников теряет смысл — ИИ-помощнику нечего
		// читать; пустой выбор формы не должен калечить чат.

		// Act: форма отдала пустой набор источников.
		const params = chatParamsFromForm({ sources: [] });

		// Assert: источники откатываются к дефолту «все три».
		expect(params.sources).toEqual(["journal", "rules-corpus", "market"]);
	});

	it("чужие идентификаторы источников в набор не попадают", () => {
		// Arrange: справочник закрытый — посторонние id из формы отбрасываются.

		// Act: форма отдала смесь известных и неизвестных идентификаторов.
		const params = chatParamsFromForm({
			sources: ["market", "unknown-source" as ChatDataSource, "market"],
		});

		// Assert: набор собран только из справочника и без повторов.
		expect(params.sources).toEqual(["market"]);
	});
});
