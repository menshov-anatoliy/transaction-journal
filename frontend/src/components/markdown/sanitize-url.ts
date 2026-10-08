// Белый список схем журнального текста: https/http и почта. Относительные
// адреса и якоря схемой не обладают и проходят проверку без списка.
const safeSchemes = new Set(["https", "http", "mailto"]);

// Управляющие символы и пробелы вырезаются до разбора схемы: браузеры
// игнорируют их внутри href, и «java\tscript:» без зачистки обошёл бы фильтр.
const controlChars = /[\u0000-\u0020]/g;

// Схема адреса — сегмент до первого двоеточия, если оно идёт раньше
// «/», «?» и «#» (иначе двоеточие принадлежит пути или якорю, а не схеме).
const schemePattern = /^([a-zA-Z][a-zA-Z0-9+.-]*):/;

// Политика ссылок журнального текста: адреса с небезопасной схемой
// вырезаются целиком, безопасные и относительные проходят без изменений.
// Применяется как urlTransform ко всем ссылкам Markdown-рендера.
// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
export function sanitizeUrl(url: string): string {
	if (url == null || url === "") {
		return "";
	}

	const analyzable = url.replace(controlChars, "");
	const match = schemePattern.exec(analyzable);
	if (match === null) {
		return url;
	}

	return safeSchemes.has(match[1].toLowerCase()) ? url : "";
}
