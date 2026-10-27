namespace TransactionJournal.Infrastructure.Tracing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

/// <summary>
/// Регистрация единого механизма операционной трассы агентского харнеса:
/// OTel SDK слушает источники обеих поверхностей (чат консультаций и проход
/// подсказок) и встроенный источник Microsoft.Extensions.AI, спаны пишутся
/// локальным JSONL-экспортёром через фоновый batch-обработчик, чтобы горячий
/// путь стрима ответа не блокировался. Трассировка всегда включена —
/// сэмплирование не настраивается. Опциональный OTLP-экспорт (dev-просмотр
/// Phoenix) подключается отдельным экспортёром по настройке приёмника.
/// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
/// </summary>
public static class AgentTracingServiceCollectionExtensions
{
	/// <summary>Имя сервиса в строках трассы и ресурсе OTLP.</summary>
	public const string ServiceName = "transaction-journal";

	/// <summary>Источник спанов хода чата консультаций.</summary>
	public const string ChatsSourceName = "TransactionJournal.Chats";

	/// <summary>Источник спанов прохода подсказок.</summary>
	public const string HintsSourceName = "TransactionJournal.Hints";

	/// <summary>Встроенный источник спанов Microsoft.Extensions.AI.</summary>
	public const string MeaiSourceName = "Experimental.Microsoft.Extensions.AI";

	/// <summary>
	/// Подключает трассировку агентского харнеса: опции читаются из секции
	/// <c>Tracing</c> конфигурации (отсутствие секции — дефолты), относительный
	/// каталог трасс разрешается от корня содержимого и создаётся на старте.
	/// Прочитанные опции регистрируются singleton-ом — потребители захвата
	/// резолвят уровень детализации из DI.
	/// </summary>
	/// <param name="services">Коллекция сервисов.</param>
	/// <param name="configuration">Конфигурация приложения.</param>
	/// <param name="contentRootPath">Корень содержимого приложения.</param>
	/// <returns>Та же коллекция сервисов для цепочки вызовов.</returns>
	/// <exception cref="ArgumentNullException">Коллекция или конфигурация не заданы.</exception>
	/// <exception cref="ArgumentException">Корень содержимого пуст.</exception>
	/// <exception cref="InvalidOperationException">Значение Tracing:Detail не распознано.</exception>
	public static IServiceCollection AddAgentTracing(
		this IServiceCollection services,
		IConfiguration configuration,
		string contentRootPath)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

		var configuredOptions = TracingOptionsReader.Read(configuration);
		var options = configuredOptions with
		{
			Dir = Path.IsPathRooted(configuredOptions.Dir)
				? configuredOptions.Dir
				: Path.Combine(contentRootPath, configuredOptions.Dir),
		};

		// Каталог трасс создаётся на старте: первый ход чата пишет в готовый файл.
		Directory.CreateDirectory(options.Dir);
		services.AddSingleton(options);

		services.AddOpenTelemetry()
			.WithTracing(tracing =>
			{
				tracing
					.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(ServiceName))
					// Самплер AlwaysOn: трассировка агента всегда включена, без
					// сэмплирования. Дефолтный ParentBased(AlwaysOn) отбрасывал бы
					// корневые спаны хода и прохода внутри HTTP-запроса ASP.NET
					// Core: их родителем выступает несэмплированная активность
					// HttpRequestIn, и наследование отбрасывало бы всю трассу
					// агента — на живом прогоне трассы не появлялись вовсе.
					// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
					.SetSampler(new AlwaysOnSampler())
					.AddSource(ChatsSourceName)
					.AddSource(HintsSourceName)
					.AddSource(MeaiSourceName)
					// Фоновая запись: спаны уходят в JSONL пакетами, стриминг
					// ответа не блокируется ожиданием диска.
					.AddProcessor(new BatchActivityExportProcessor(new TraceJsonlExporter(options, ServiceName)));

				// Dev-опция просмотра: Phoenix принимает OTLP по gRPC; приёмник
				// не задан — экспорт полностью выключен, трассы живут только в
				// локальном JSONL, внешних зависимостей нет.
				// Traceability: openspec:ops/agent-tracing#requirement-trace-phoenix-dev-option
				// Traceability: openspec:ops/agent-tracing#scenario-trace-phoenix-off-by-default
				if (options.OtlpEndpoint is { } otlpEndpoint)
				{
					tracing.AddOtlpExporter(otlp =>
					{
						otlp.Endpoint = new Uri(otlpEndpoint);
						otlp.Protocol = OtlpExportProtocol.Grpc;
					});
				}
			});

		return services;
	}
}
