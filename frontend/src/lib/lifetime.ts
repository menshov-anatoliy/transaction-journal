// Порог зоны предупреждения экспирации, суток: совпадает с порогом
// предупреждения вычислителя метрик времени жизни в аналитике. Клиент
// красит значение остатка суток и подсвечивает строку опционной ноги
// сам — отдельный флаг в контракте для этого не нужен.
// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-list-expiry-days-red
export const EXPIRY_WARNING_DAYS = 14;
