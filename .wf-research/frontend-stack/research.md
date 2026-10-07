# Research: выбор frontend-стека и компонентной базы (issue #51)

Часть wayfinder-карты #50. Цель — обосновать для ADR выбор SPA-фреймворка, компонентной базы, AI-чат решения, markdown-инструментов, таблиц и графиков нового UI журнала сделок.

**Дата проверки фактов: 2026-10-07.** Все версии пакетов — из npm registry (запрос `npm view` в этот день); активность, звёзды и лицензии — из GitHub API (`gh api`); качественные утверждения — из официальной документации проектов (см. «Источники»). Утверждения, не проверенные напрямую, помечены как *предположение*.

---

## 1. Контекст и требования

- Однопользовательский журнал торговли на Bybit; новый SPA поверх нового HTTP API существующего .NET-бэкенда (решение карты #50). Blazor удаляется после паритета. SSR/SEO не нужны.
- UI-задачи: AI-чат (левая панель с историей «активные/завершённые», пустое состояние с карточками-подсказками, крупный многострочный ввод с выбором модели и подключением источников/tools, стриминг SSE); разделы Конструкции / Карточка конструкции / Агент / Синхронизация; таблицы и графики PnL/позиций; каталог правил с фильтрами и попапом при наведении; markdown-вьюер везде + редактор «тулбар + live-превью (split-view, по образцу GitHub), без WYSIWYG».
- Дизайн-референс (комментарий в OOO-IT-MOLCOM/Molcom.Warehouse.Web#585, 2026-10-01): светлый «воздушный» каркас, крупные отступы, сдержанный акцентный цвет — т.е. **кастомный минимализм, а не готовая брендированная тема**.
- Платформы: responsive web, desktop + мобильный браузер.
- Критично: доступность доков библиотек через context7 (MCP) для будущей генерации кода агентом.

## 2. Критерии сравнения

1. Скорость разработки для solo-разработчика (готовые решения под наши конкретные задачи, качественные LLM-знания по стеку).
2. Наличие готовых AI-чат компонентов/библиотек (в т.ч. под кастомный бэкенд с SSE).
3. Markdown-инструменты: вьюер + редактор (тулбар, split-превью, без WYSIWYG).
4. Кастомизируемость дизайна под светлый «воздушный» референс.
5. Экосистема и долгосрочная поддержка.
6. Доступность доков через context7.

Оценки «скорость/удобство» — качественные экспертные суждения (помечены в тексте), а не измерения.

## 3. Сводный вердикт

**React 19 + Vite SPA (TypeScript) + Tailwind CSS 4 + shadcn/ui; AI-чат — assistant-ui (runtime поверх нашего SSE API) с опорой на Vercel AI SDK примитивы и реестр AI Elements; вьюер — react-markdown (+remark-gfm); редактор — @uiw/react-md-editor (split-режим); таблицы — TanStack Table + shadcn table; графики — Recharts.** Подробное обоснование и trade-off — в разделах 4–10.

---

## 4. Сравнение SPA-фреймворков

Проверенные факты (npm + GitHub, 2026-10-07):

| | React | Angular | Vue 3 | Svelte 5 (+Kit) |
|---|---|---|---|---|
| Актуальная версия | 19.3.0 | 22.2.1 | 3.5.43 (3.6 в бете) | 5.57.2 / Kit 3.0.1 |
| Репозиторий | react/react (бывш. facebook/react) | angular/angular | vuejs/core (+nuxt/nuxt) | sveltejs/svelte (+sveltejs/kit) |
| Звёзды | ~250 900 | ~101 000 | ~54 600 (+Nuxt ~60 900) | ~88 300 (+Kit ~20 800) |
| Лицензия | MIT | MIT | MIT | MIT |
| Последний push | 2026-10-06 | 2026-10-07 | 2026-10-07 | 2026-10-06 |
| Последний релиз | v19.3.0 (2026-09-09) | v22.2.1 (2026-09-30) | v3.5.43 (2026-09-17) | svelte@5.57.2 (2026-10-06) |

Все четыре живы, активны и MIT. Различия — не в «живости», а в экосистеме под наши задачи.

### 4.1. Готовые AI-чат компоненты (ключевой критерий)

**React — безусловный лидер, проверено:**

- **assistant-ui** (`@assistant-ui/react` 0.15.25): 12 426★, MIT, репозиторий переехал в собственный org `assistant-ui/assistant-ui`, релизы еженедельно (последний 2026-10-06). Компоненты + рантаймы + примитивы: ThreadList (левая панель истории), Composer (ввод), стриминг, Tool UI, генеративный UI, ветвление. Явно поддерживает «свой сервер»: LocalRuntime (один метод `ChatModelAdapter.run` — достаточно `fetch` к нашему SSE), ExternalStoreRuntime (наш стор в TanStack Query/zustand), DataStream-протокол. Построен на shadcn/Radix+Base UI + Tailwind — т.е. на том же стеке, что и рекомендуемая компонентная база.
- **Vercel AI SDK** (`ai` 7.0.130): 27 156★, ядро Apache-2.0. Официальные биндинги `@ai-sdk/react` 4.0.133, `@ai-sdk/vue` 4.0.130, `@ai-sdk/svelte` 5.0.130, **`@ai-sdk/angular` 3.0.130** — т.е. хуки useChat доступны во всех четырёх фреймворках (проверено npm).
- **AI Elements** (`ai-elements` 1.9.0): репозиторий `vercel/ai-elements`, 2 476★, Apache-2.0, push 2026-09-01. Библиотека shadcn-компонентов для чатов (conversation, message, prompt input и т.д.) — устанавливаются в проект как реестр shadcn.
- **CopilotKit** (1.77.0): 37 792★, MIT. «Frontend Stack for Agents & Generative UI. React, Angular, Mobile…». В монорепе есть `packages/angular` (проверено деревом репозитория) — но это тяжёлый агентный фреймворк (AG-UI, cloud), избыточен для своего .NET API.
- **chatscope** (`@chatscope/chat-ui-kit-react` 2.1.1): репозиторий `chatscope/chat-ui-kit-react`, 1 780★, MIT, последний push **2025-05-15** — стагнирует. Не рекомендуется.

**Vue:** `@assistant-ui/vue` существует (та же архитектура: provider + композаблы + примитивы + реестр shadcn-vue), но пакет новее и урезан: официально «Not in the Vue package»: голос, диктовка, mentions, slash-команды, цитирование, модальный ассистент, DevTools; CLI не скаффолдит Vue-проекты (документация assistant-ui, проверено 2026-10-07). Плюс `@ai-sdk/vue`. Поиск GitHub «ai chat vue» даёт только 0-звёздные хобби-репозитории (проверено). Часть урезанного нам не нужна (голос), но зрелость поверхности ниже, чем у React.

**Angular:** `@ai-sdk/angular` (официальный) + CopilotKit `packages/angular`. Нет ни assistant-ui, ни AI Elements.

**Svelte:** только `@ai-sdk/svelte` и порт AI Elements от сообщества (`sikandarjodd/ai-elements`, низкая зрелость — виден в context7 как сторонний реестр).

### 4.2. Markdown, таблицы, графики

- React: вьюер react-markdown 10.1.0 (15 904★, MIT; экосистема remark/rehype: remark-gfm 4.0.1, remark-math, rehype-highlight); редакторы под требование «тулбар + split, без WYSIWYG» — `@uiw/react-md-editor` 4.1.2 (2 932★, MIT, push 2026-08-21) и ваннилла-Vditor (встраивается куда угодно); таблицы TanStack Table v9 (headless, адаптеры React/Vue/Solid/Svelte); графики Recharts 3.10.1.
- Vue: shadcn-vue 2.8.2 + bits-ui 2.19.5; markdown-вьюеры — обёртки над markdown-it/marked; таблицы — TanStack/vue-table или PrimeVue DataTable; графики — vue-echarts 8.3.1 / vue-chartjs. Покрытие есть, но выбор уже и проверка каждого звена — на нас.
- Angular:ngx-markdown 22.1.0 (marked-based) для вьюера; PrimeNG DataTable / Angular Material CDK table; ng-apexcharts / chart.js обёртки. Готового split-редактора с тулбаром в экосистеме нет — только ваннилла (Vditor/Cherry).
- Svelte: marked/svelte-markdown, bits-ui-стек; специализированных MD-редакторов нет.

### 4.3. Кастомизация дизайна под референс

- React + shadcn/ui: компоненты — исходный код в нашем репозитории (copy-in через CLI), тема через CSS-переменные Tailwind. Максимальная свобода под «воздушный» кастомный каркас; assistant-ui/AI Elements используют тот же визуальный язык.
- Vue: shadcn-vue 2.8.2 + bits-ui — подход тот же, реестр меньше оригинала (*предположение по объёму: judging по номерам версий/датам*).
- Angular: Angular Material (25 035★, v22.2.1) — Material 3, темизация, но уход от «Material-look» дороже; PrimeNG (12 483★) — богатые виджеты, свой выраженный стиль.
- Svelte: shadcn-svelte 1.7.0 + bits-ui 2.19.5 — прилично, но меньше готовых блоков.

### 4.4. Скорость разработки для solo (экспертная оценка)

- Vue и Svelte мягче по входу и лаконичнее; Angular даёт «батарейки» (роутер, формы, DI, HTTP, тестируемость) и типизацию, знакомую .NET-разработчику, но церемоний больше всех.
- Однако для **этого** проекта время съедает не фреймворк, а кастомные куски: AI-чат с историей/tools/SSE, MD-редактор, каталог правил, таблицы/графики. По каждому из них готовых решений у React больше и они зрелее (см. 4.1–4.2) — суммарно React здесь быстрее для solo, несмотря на необходимость самому собрать роутинг/стейт из библиотек. LLM-ассистенты (включая нашу будущую реализацию через context7) знают React+shadcn+Tailwind лучше всего (качественное суждение; косвенное подтверждение — покрытие в context7, раздел 4.6).

### 4.5. Экосистема и долгосрочная поддержка

- React: крупнейшее сообщество (~250k★), наибольшее количество библиотек/шаблонов/ответов; риск «моды» на конкретные библиотеки компенсируется выбором консервативных зависимостей.
- Angular: Google, предсказуемый 6-месячный каденс + LTS — лучший для enterprise-команд, для solo избыточен.
- Vue: устойчивый независимый проект (+Nuxt Labs); средняя экосистема.
- Svelte: отличный DX, но наименьшая экосистема компонентов/найма.

### 4.6. Доступность доков через context7 (проверено resolve 2026-10-07)

| Библиотека | context7 ID (лучший результат) | Сниппетов |
|---|---|---|
| React | /reactjs/react.dev (и зеркала) | 4 342–4 980 |
| Angular | /websites/angular_dev | 13 439 |
| Vue | /websites/vuejs (+llms-зеркала) | 1 990–5 251 |
| Svelte | /websites/svelte_dev (+llms) | 8 026–11 930 |
| shadcn/ui | /shadcn-ui/ui | 4 138 (версии вкл. 4.21) |
| assistant-ui | /assistant-ui/assistant-ui | 10 664 (+ зеркала до 21 168) |
| Vercel AI SDK | /vercel/ai, /websites/ai-sdk_dev | 8 074–8 787 |
| AI Elements | /vercel/ai-elements | 1 558 |
| TanStack Table | /tanstack/table | 9 072 |
| Mantine | /websites/mantine_dev | 15 704 |
| Recharts | /websites/recharts_github_io | 848 |
| MDXEditor | /mdx-editor/editor | 631 |
| Tiptap | /ueberdosis/tiptap-docs | 7 680 |
| Milkdown | /websites/milkdown_dev | 847 |
| @uiw/react-md-editor | /uiwjs/react-md-editor | 691 |
| Vditor | /vanessa219/vditor | **73** (слабо) |

Сами фреймворки покрыты у всех четырёх; существенно отличается покрытие **прикладных** библиотек: React-стек (shadcn, assistant-ui, AI SDK, TanStack, Recharts) — от хорошего до отличного; Vditor в context7 почти не представлен.

**Итог по фреймворкам: React.** Vue — достойный второй (assistant-ui Vue существует, но урезан; shadcn-vue хорош). Angular и Svelte проигрывают именно по AI-чат готовым компонентам — ключевому критерию.

---

## 5. Компонентные базы (React)

Проверено (npm + GitHub, 2026-10-07):

| База | Версия | Звёзды | Лицензия | Последний релиз/push | Форм-фактор |
|---|---|---|---|---|---|
| **shadcn/ui** (CLI `shadcn`) | 4.21.3 | 125 227 | MIT | 2026-10-06 | copy-in реестр на Radix/Base UI + Tailwind v4 |
| Mantine | 9.7.1 | 31 804 | MIT | 2026-10-06 | классическая библиотека, 120+ компонентов, 70+ хуков |
| MUI (`@mui/material`) | 9.4.0 | 99 139 | MIT | 2026-08-28 | библиотека, Material-look |
| Ant Design | 6.6.5 | 99 699 | MIT | 2026-09-20 | библиотека, enterprise-look, CSS-in-JS |
| PrimeReact | 11.2.0 (npm) | 8 309 | MIT | 2026-09-24 (GH rel 10.9.9 2026-08-27) | библиотека, богатые виджеты, тематизация |

Анализ под наши критерии:

- **shadcn/ui** — лучший под референс «светлый воздушный кастомный каркас»: компоненты принадлежат нам (нет вендор-стиля), тема = CSS-переменные, Tailwind 4; идеальная совместимость с assistant-ui и AI Elements (оба построены на shadcn). Минусы: это не «поставил и забыл» — код компонентов живёт в нашем репо и обновляется через CLI; нет тяжёлых виджетов (продвинутый data grid, date-range picker — добирать community-реестрами или самому).
- **Mantine** — самый дисциплинированный альтернативная база (21 открытый issue — редкая аккуратность), богатый набор, отличные доки и context7. Минус: собственный визуальный язык — под референс придётся перетемировать; AI-чат стек assistant-ui/AI Elements с Mantine напрямую не совпадает (они заточены под Tailwind/shadcn).
- **MUI** — зрелость и DataGrid (community-версия), но Material-эстетика и Emotion — лишняя работа под кастомный дизайн; DataGrid Pro — платный.
- **Ant Design** — «энтерпрайз-плотный» дизайн, тяжёлый рантайм стилей; плохо совпадает с «воздушным» референсом.
- **PrimeReact** — функционально богат (DataTable), но самобытный Prime-стиль; лучше как источник отдельных виджетов, чем как база.

**Выбор: shadcn/ui** (Radix-флейвор), иконки lucide-react 1.52.0. Обоснование: критерий 4 (кастомизация) + синергия с AI-чат стеком.

---

## 6. AI-чат решения

| Решение | Версия | Звёзды | Лицензия | Активность | Что даёт |
|---|---|---|---|---|---|
| **assistant-ui** | 0.15.25 | 12 426 | MIT | релизы еженедельно, 2026-10-06 | полный чат-UX: Thread/Composer/ThreadList, стриминг, ветвление, Tool UI, ветки; рантаймы поверх любого бэкенда |
| Vercel AI SDK (`ai` + `@ai-sdk/react`) | 7.0.130 / 4.0.133 | 27 156 | Apache-2.0 (ядро) | 2026-10-07 | примитивы: useChat, UI Message stream протокол (SSE), провайдеры LLM |
| AI Elements (`ai-elements`) | 1.9.0 | 2 476 | Apache-2.0 | 2026-09-01 | shadcn-реестр готовых чат-компонентов (conversation, prompt input, response) |
| CopilotKit | 1.77.0 | 37 792 | MIT | 2026-10-02 | агентный фреймворк (AG-UI, cloud); React+Angular; тяжёл для нашего случая |
| chatscope | 2.1.1 | 1 780 | MIT | push 2025-05-15 — стагнирует | простой чат-кит; не рекомендуется |

Сценарий у нас специфический: **бэкенд свой (.NET), SSE свой, история чатов своя** (домен #19). Проверено по документации assistant-ui (раздел Custom Runtime): четыре пути — `LocalRuntime` (пишем один `ChatModelAdapter.run` с `fetch` к нашему SSE — assistant-ui сам ведёт состояние), `ExternalStoreRuntime` (сообщения живёт в нашем сторе — TanStack Query/zustand), `DataStream` (бэкенд отдаёт стандартный протокол message-parts), `AssistantTransport` (снапшоты состояния агента). Это покрывает: левую панель (ThreadList + «активные/завершённые» — группировка на нашей стороне), пустое состояние (свой лэйаут + Suggestion-адаптер), выбор модели и tools в Composer (кастомизация компонентов реестра).

**Рекомендация: assistant-ui как каркас чата** + Vercel AI SDK как источник протокольных примитивов при необходимости (например, формат UIMessage-потока) + AI Elements как дополнительный реестр готовых блоков (prompt input и пр.). CopilotKit — нет (тянет свою инфраструктуру AG-UI/cloud; наш бэкенд уже написан). chatscope — нет (стагнация).

Замечание по зрелости: assistant-ui **pre-1.0** (0.15.x) — API может меняться. Смягчения: компоненты устанавливаются в проект (обновления контролируемы, как в shadcn), рантайм-интерфейсы (`ChatModelAdapter`, External Store) документированы как стабильные контракты, есть гайды миграций. Для однопользовательского приложения риск приемлем.

---

## 7. Markdown: вьюер и редактор

Требование: рендер журнального текста везде; редактирование — тулбар + live-превью (split-view, GitHub-стиль), **без WYSIWYG**.

### 7.1. Вьюеры

| Библиотека | Версия | Звёзды | Лицензия | Статус |
|---|---|---|---|---|
| **react-markdown** | 10.1.0 | 15 904 | MIT | стабильно; последний релиз 2025-03-07 (зрелый remark-стек) |
| markdown-it | 15.0.2 | 21 961 | MIT | поддерживается (push 2026-09-12) |
| marked | 18.1.0 | — | MIT | активно |

**Выбор: react-markdown + remark-gfm** (таблицы/списки-задачи GFM нужны журналу), опционально rehype-highlight (код в ответах агента) и remark-math (если появятся формулы). Идиоматично для React, безопасный рендер по умолчанию, единый рендер-путь для чата, карточек правил и комментариев.

### 7.2. Редакторы (тулбар + split-превью)

| Редактор | Версия | Звёзды | Лицензия | Статус | Соответствие «split + тулбар, без WYSIWYG» |
|---|---|---|---|---|---|
| **@uiw/react-md-editor** | 4.1.2 | 2 932 | MIT | push 2026-08-21 — жив | **да**: textarea + тулбар + режимы split/edit/preview |
| Vditor | 4.0.0 | 11 364 | MIT | v4.0.0 2026-08-30 — жив | да: режим `sv` (split-view), настраиваемый тулбар (проверено по README) |
| ByteMD | 1.22.0 | 1 374 | MIT | **заморожен с 2025-02-12** (репо переехал к автору pd4d10) | да, но мёртв — не брать |
| Cherry-markdown (Tencent) | 0.11.10 | 4 887 | Apache-2.0 (с исключениями для третьих компонентов — проверено по LICENSE) | push 2026-08-24 — жив | да (split + тулбар); ваннилла-ядро |
| MDXEditor | 4.3.2 | 3 690 | MIT | push 2026-10-02 — жив | **нет**: Lexical-редактор «умного» WYSIWYG над markdown |
| Milkdown | 7.22.2 | 11 975 | MIT | жив | частично: ProseMirror/Typora-подход (гибридный WYSIWYG), Crepe-пресет — не GitHub-split |
| Tiptap | 3.31.4 | 38 650 | MIT (ядро; cloud — платно) | жив | нет: headless WYSIWYG rich-text |
| EasyMDE | 2.21.0 | — | MIT | низкая активность | да, но устаревает |

**Выбор: @uiw/react-md-editor** — точное попадание в требование (split + тулбар без WYSIWYG), активен, MIT, лёгкий в рестайлинге (минимализм), хорошая поддержка в context7 (691 сниппет); превью-движок этой же экосистемы (@uiw/react-markdown-preview 5.2.1) унифицируется с react-markdown. Запасные варианты: **Vditor** (богаче тулбар: outline, экспорт, темы; минусы — китайско-центричная документация и почти нулевое покрытие в context7: 73 сниппета) и **DIY: @uiw/react-codemirror 4.25.12 + react-markdown-превью** (полный контроль дизайна за ~пару дней работы, все зависимости активны) — если рестайлинг uiw под референс окажется мучительным.

---

## 8. Таблицы и графики

### 8.1. Таблицы

| Библиотека | Версия | Звёзды | Лицензия | Форм-фактор |
|---|---|---|---|---|
| **TanStack Table** (`@tanstack/react-table`) | 9.2.6 | 28 477 | MIT | headless-движок (React/Vue/Solid/Svelte), push 2026-10-04 |
| AG Grid Community | 36.2.0 | — | MIT (community; Enterprise платно) | полный грид |
| MUI X DataGrid | 9.15.0 | — | MIT (community; Pro платно) | грид для MUI |

Объёмы данных личного журнала (сотни конструкций/позиций) не требуют тяжёлых гридов. **Выбор: TanStack Table v9 + таблица из shadcn/ui** (официальный data-table рецепт shadcn построен на TanStack) — сортировка/фильтрация/выделение строк под каталог правил и списки позиций, полный контроль вида.

### 8.2. Графики (PnL, позиции)

| Библиотека | Версия | Звёзды | Лицензия | Примечание |
|---|---|---|---|---|
| **Recharts** | 3.10.1 | 27 617 | MIT | компонуемые SVG-графики, push 2026-10-06; лёгкий «воздушный» стиль |
| ECharts | 6.1.0 | 67 459 | Apache-2.0 | мощнейший; canvas/svg; heavy для простых задач |
| Chart.js | 4.5.1 | 67 733 | MIT | canvas, через react-chartjs-2 |

**Выбор: Recharts** — декларативные React-компоненты, SVG легко темируется под светлый минимализм, достаточно для PnL-кривых, столбчатых и стековых диаграмм позиций. ECharts держим как запас при потребности в сложных типах (тепловые карты досок опционов и т.п.) — *предположение о будущих потребностях*.

## 9. SSE-стриминг

Библиотека не нужна:

- Браузер: `fetch` + `ReadableStream` (POST с телом — для отправки сообщений в чат) или `EventSource` (только GET, авто-реконнект — для каналов прогресса синхронизации). Это нативные API, оба пути покрыты документацией MDN.
- assistant-ui: `LocalRuntime` принимает async-итератор/`async *run` — место, где мы парсим наш SSE и отдаём дельты; т.е. стриминг инкапсулируется в одном адаптере.
- .NET-сторона (из плана #50): обычный `text/event-stream` из Minimal API — на выбор фреймворка не влияет.

Готовых выводов по бенчмаркам не делаем (не измеряли).

---

## 10. Итоговая рекомендация

| Слой | Выбор | Версия (npm, 2026-10-07) | Почему |
|---|---|---|---|
| Фреймворк | **React + TypeScript, Vite SPA** | react 19.3.0, vite 8.3.3, typescript 7.0.2 | единственный фреймворк с полным покрытием наших нестандартных требований (AI-чат, MD, кастомный дизайн); SSR не нужен — Next.js лишний слой |
| Роутинг/данные | react-router, TanStack Query | 8.4.0 / 5.104.1 | стандарт де-факто для SPA; Query кэширует HTTP API и держит стор для ExternalStoreRuntime |
| UI-база | **shadcn/ui + Tailwind CSS 4 + lucide-react** | shadcn 4.21.3, tailwindcss 4.3.3 | полный контроль под «воздушный» референс; общий визуальный язык с assistant-ui и AI Elements |
| AI-чат | **assistant-ui** (+ Vercel AI SDK примитивы, AI Elements реестр по надобности) | @assistant-ui/react 0.15.25, ai 7.0.130, ai-elements 1.9.0 | ThreadList/Composer/стриминг/Tool UI из коробки; рантайм поверх нашего .NET SSE одним адаптером; MIT/Apache |
| MD-вьюер | **react-markdown + remark-gfm** | 10.1.0 / 4.0.1 | единый рендер журнального текста (чат, правила, комментарии), GFM |
| MD-редактор | **@uiw/react-md-editor** (split) — запас: Vditor / DIY CodeMirror+react-markdown | 4.1.2 | точное соответствие «тулбар + split, без WYSIWYG», активен, легко темизируется |
| Таблицы | **TanStack Table + shadcn table** | 9.2.6 | headless = полный контроль вида; достаточно на наши объёмы |
| Графики | **Recharts** (запас: ECharts) | 3.10.1 / 6.1.0 | SVG-минимализм под светлый дизайн; покрывает PnL/позиции |
| Стриминг | нативный fetch + ReadableStream / EventSource | — | без зависимостей; инкапсулируется в адаптер assistant-ui |

### Чем жертвуем (осознанные компромиссы)

1. **Простота и компактность кода** (Svelte/Vue выигрывают) — платим бойлерплейтом и необходимостью курировать зависимости ради готовых AI-чат/MD решений.
2. **«Батарейки» Angular** (роутер, формы, DI из коробки, TS-дисциплина, знакомая .NET-разработчику) — собираем сами из react-router + TanStack-стека.
3. **Вендорная целостность shadcn/ui** — код компонентов в нашем репо: обновления и совместимость — наша забота (смягчено CLI-реестром).
4. **Pre-1.0 у assistant-ui** — возможные breaking changes (смягчения см. раздел 6).
5. **Функциональная простота @uiw/react-md-editor** — богаче был бы Vditor/Tiptap, но они конфликтуют с требованием «без WYSIWYG» (Tiptap/Milkdown/MDXEditor) или с доками/context7 (Vditor).
6. **Скромность Recharts** — экзотические типы визуализаций потребуют ECharts точечно.
7. **Никакого Material/enterprise-look из коробки** — «воздушный» референс собираем сами на CSS-переменных (это и была цель).

## 11. Риски и митигации

- **Churn AI SDK / assistant-ui**: изоляция в одном адаптере (ChatModelAdapter/ExternalStore); протокол SSE наш — фреймворк-зависимого кода в домене нет.
- **Radix → Base UI миграция в экосистеме shadcn** (assistant-ui уже раздаёт оба флейвора): фиксируем один флейвор в ADR.
- **Рестайлинг редактора**: если uiw не ляжет на референс — план Б (Vditor или DIY на CodeMirror 6) не меняет остальной стек.
- **Одиночная шина найма/знаний**: React — самый популярный стек, риск минимален; обратный риск Vue/Svelte — меньше примеров именно для AI-чата.
- **Мобильный браузер**: responsive обеспечивается Tailwind + примитивами Radix; чат-компоновки (левая панель → drawer) — стандартные паттерны assistant-ui (модальный/drawer-режимы у них есть для React).

## 12. Альтернатива, если React будет отвергнут

**Vue 3 + Vite + Tailwind + shadcn-vue/bits-ui + @assistant-ui/vue + @ai-sdk/vue + markdown-it/vue-реестры + TanStack vue-table + Recharts-аналог (vue-echarts/Chart.js)**. Законный запасной вариант: assistant-ui для Vue существует, но поверхность урезана и моложе; проверять придётся больше звеньев самостоятельно. Angular/Svelte для этого конкретного набора требований не рекомендуются (нет assistant-ui/AI Elements вовсе).

---

## 13. Источники

Всё проверено 2026-10-07:

- npm registry: версии `react`, `@angular/core`, `vue`, `svelte`, `@sveltejs/kit`, `nuxt`, `tailwindcss`, `vite`, `typescript`, `react-router`, `@tanstack/react-query`, `shadcn`, `@mantine/core`, `@mui/material`, `antd`, `primereact`, `primevue`, `primeng`, `@angular/material`, `@assistant-ui/react`, `@copilotkit/react-core`, `ai`, `@ai-sdk/react|vue|svelte|angular`, `ai-elements`, `@chatscope/chat-ui-kit-react`, `react-markdown`, `remark-gfm`, `markdown-it`, `marked`, `@mdxeditor/editor`, `@milkdown/core`, `@milkdown/crepe`, `@tiptap/core`, `vditor`, `@uiw/react-md-editor`, `@uiw/react-codemirror`, `@uiw/react-markdown-preview`, `@tanstack/react-table`, `recharts`, `chart.js`, `echarts`, `ag-grid-community`, `@mui/x-data-grid`, `vuetify`, `element-plus`, `naive-ui`, `shadcn-vue`, `bits-ui`, `shadcn-svelte`, `ng-zorro-antd`, `ngx-markdown`, `lucide-react`, `easymde`, `cherry-markdown`, `vue-echarts`, `ng-apexcharts`, `apexcharts` (даты релизов — поле `time`).
- GitHub API: репозитории react/react, angular/angular, vuejs/core, nuxt/nuxt, sveltejs/svelte, sveltejs/kit, shadcn-ui/ui, mui/material-ui, mantinedev/mantine, ant-design/ant-design, primefaces/primereact, primefaces/primevue, primefaces/primeng, angular/components, assistant-ui/assistant-ui, CopilotKit/CopilotKit (в т.ч. дерево `packages/angular`), vercel/ai, vercel/ai-elements, chatscope/chat-ui-kit-react, remarkjs/react-markdown, markdown-it/markdown-it, mdx-editor/editor, Milkdown/milkdown, pd4d10/bytemd, ueberdosis/tiptap, Vanessa219/vditor, uiwjs/react-md-editor, Tencent/cherry-markdown, TanStack/table, recharts/recharts, chartjs/Chart.js, apache/echarts (звёзды, лицензии, даты push/релизов; лицензии PrimeNG/vercel-ai/cherry сверены по файлам LICENSE).
- Документация assistant-ui: `assistant-ui.com/llms.txt`, `/docs.md`, `/docs/vue.md`, `/docs/runtimes/custom/overview.md` (поддержка React/Vue/RN/Ink; ограничения Vue-пакета; четыре пути Custom Runtime).
- README Vditor (режимы `sv/ir/wysiwyg`, конфигурация тулбара, MIT, English README).
- Дизайн-референс: комментарий 5928016214 в OOO-IT-MOLCOM/Molcom.Warehouse.Web#585 (2026-10-01) — «светлый воздушный каркас… сдержанный акцентный цвет».
- context7 (MCP): resolve по React, Angular, Vue, Svelte, shadcn/ui, Mantine, assistant-ui, Vercel AI SDK, AI Elements, TanStack Table, MDXEditor, Recharts, Tiptap, Milkdown, Vditor, «React MD Editor» (количества сниппетов в разделе 4.6).
- Поиск GitHub («ai chat vue», «svelte ai chat components») — для проверки пустоты ниши AI-чат компонентов вне React.
