# Tasks

## 1. Инфраструктура загрузки .env

- [x] 1.1 Добавить NuGet-пакет `DotNetEnv` в `src/TransactionJournal/TransactionJournal.csproj` (версию зафиксировать точечно) и убедиться, что `dotnet restore` и сборка решения проходят без ошибок
- [x] 1.2 Создать статический класс `AppEnvFile` в `src/TransactionJournal/AppEnvFile.cs` с методом `Load()`, инкапсулирующим `DotNetEnv.Env.TraversePath().NoClobber().Load()` (решение D3, D4): поиск `.env` от текущего каталога вверх, без перезаписи существующих переменных; убедиться, что сборка проходит

## 2. Интеграция и шаблон репозитория

- [x] 2.1 Вызвать `AppEnvFile.Load()` первой строкой логики `Program.cs` до `WebApplication.CreateBuilder` (решение D2) с комментарием о смысле и меткой `Traceability: openspec:config/env-file#requirement-env-file-loaded-on-startup`; убедиться, что приложение запускается
- [x] 2.2 Создать `.env.example` в корне репозитория с заглушками `BYBIT_API_KEY=` и `BYBIT_API_SECRET=` и русскими комментариями о назначении, размещении и приоритете системных переменных (требование `requirement-env-file-stays-local`); проверить `git status -- .env.example` видит файл, а `git check-ignore .env` подтверждает игнор реального `.env`

## 3. Модульные тесты загрузчика

- [ ] 3.1 Добавить тесты `AppEnvFile` в `tests/TransactionJournal.Tests/AppEnvFileTests.cs`: во временном каталоге создаётся `.env` с `BYBIT_API_KEY`/`BYBIT_API_SECRET`, текущий каталог переключается, после `Load()` значения видны через `Environment.GetEnvironmentVariable` и доезжают до `EnvironmentBybitCredentialsProvider.GetCredentials()` (сценарий `scenario-env-values-become-process-vars`); очистка каталога и переменных в Initialize/Cleanup; убедиться, что тесты проходят
- [ ] 3.2 Добавить тест приоритета: переменная, заранее заданная в окружении, не перезаписывается значением из `.env` (сценарий `scenario-system-var-wins-over-env-file`); убедиться, что тест проходит
- [ ] 3.3 Добавить тест отсутствия файла: `Load()` во временном пустом каталоге не выбрасывает исключений и не меняет переменные (сценарий `scenario-missing-env-file-is-normal`); убедиться, что тест проходит

## 4. Проверка изменения

- [ ] 4.1 Прогнать полный набор тестов решения (`dotnet test`) и убедиться, что существующие тесты `EnvironmentBybitCredentialsProviderTests` не затронуты, а новые проходят
- [ ] 4.2 Smoke-проверка вручную: скопировать `.env.example` в `.env`, вписать тестовые значения, запустить приложение из каталога проекта и убедиться, что блок подключения на «Настройках» показывает маску ключа из `.env`, а после удаления `.env` приложение запускается без ошибок
