# Tasks

## 1. Хелпер локального отображения

- [x] 1.1 Создать в `src/TransactionJournal/Components` статический хелпер `DisplayTime`: конвертация `ToLocalTime` и константы форматов `yyyy-MM-dd HH:mm` и `yyyy-MM-dd`; снабдить traceability-меткой `openspec:ui/screens#requirement-local-time-display` с человекочитаемым комментарием о конвертации только на слое отображения
- [x] 1.2 Проверить юнит-тестами в `tests/TransactionJournal.Tests/Ui`: UTC-мгновение из unix-мс рендерится локальной стеночной частью; календарный день UTC-мгновения — локальным днём

## 2. Экраны

- [x] 2.1 `Constructions.razor`: `FormatDate` переводит открытие/закрытие и отметку марок в локальное время через хелпер
- [x] 2.2 `Inbox.razor`: `FormatTime` показывает время исполнения сделок в локальном времени; фильтр дат не меняется (уже локальный)
- [x] 2.3 `Settings.razor`: `FormatDate` для запусков синхронизации и отрисовка предупреждений сверки — в локальном времени; формат `"u"` заменён на `"yyyy-MM-dd HH:mm"`
- [x] 2.4 `Sync.razor`: предупреждения сверки экспираций — в локальном времени; формат `"u"` заменён
- [x] 2.5 `ConstructionDetail.razor`: `FormatDate` и `FormatDay` через хелпер — таблицы сделок и закрывающих записей, корректировки, период, отметка марок; предзаполнение форм правки (`_editTimeInput`, `_editAdjustDateInput`) остаётся согласованным с разбором ввода
- [x] 2.6 Снабдить изменённые форматтеры traceability-метками требования и сценария `scenario-sync-dates-shown-local` / `scenario-entry-edit-preserves-instant` с человекочитаемыми комментариями; проверить rg-поиском, что ссылки разрешаются в `Traceability ID` delta-spec

## 3. Проверки

- [x] 3.1 bUnit-тест: страница с UTC-датой из заглушки рендерит локальное представление и не содержит UTC-представления (при ненулевом смещении зоны)
- [x] 3.2 Прогон `dotnet build` и `dotnet test`; проверка `openspec validate --change add-local-time-display --strict`
