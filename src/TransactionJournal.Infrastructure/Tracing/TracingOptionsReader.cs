using TransactionJournal.Infrastructure.Ops;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TransactionJournal.Infrastructure.Tracing;

/// <summary>
/// Читает конфигурацию журнала трасс агентского харнеса при старте приложения —
/// секцию <c>Tracing</c> в стиле <see cref="BackupOptionsReader"/>. Приложение
/// работает без секции — все параметры принимают дефолты кода: уровень
/// детализации <c>detailed</c>, каталог <c>App_Data/traces</c>, ротация при
/// 10 МБ, 5 хранимых архивов, приёмник OTLP выключен. Нечисловые или
/// неположительные лимиты откатываются к дефолтам; неизвестное значение
/// уровня детализации — исключение с понятным сообщением: молчаливая подмена
/// уровня скрыла бы потерю событий аргументов тулов.
/// Traceability: openspec:config/tracing#requirement-tracing-config-section
/// Traceability: openspec:config/tracing#scenario-tracing-config-defaults
/// Traceability: openspec:config/tracing#scenario-tracing-config-switch-without-code
/// </summary>
public static class TracingOptionsReader
{
	/// <summary>Ключ уровня детализации трассы.</summary>
	public const string DetailKey = "Tracing:Detail";

	/// <summary>Ключ каталога трасс.</summary>
	public const string DirectoryKey = "Tracing:Dir";

	/// <summary>Ключ предела размера файла трасс до ротации, байты.</summary>
	public const string MaxFileBytesKey = "Tracing:MaxFileBytes";

	/// <summary>Ключ числа хранимых архивов.</summary>
	public const string KeepFilesKey = "Tracing:KeepFiles";

	/// <summary>Ключ приёмника OTLP (dev-опция Phoenix).</summary>
	public const string OtlpEndpointKey = "Tracing:OtlpEndpoint";

	/// <summary>Дефолтный каталог трасс — рядом с базами журнала.</summary>
	public const string DefaultDirectory = "App_Data/traces";

	/// <summary>Дефолтный предел размера файла трасс до ротации — 10 МБ.</summary>
	public const long DefaultMaxFileBytes = 10_485_760;

	/// <summary>Дефолтное число хранимых архивов.</summary>
	public const int DefaultKeepFiles = 5;

	/// <summary>Собирает опции журнала трасс из конфигурации; отсутствующая секция даёт дефолты.</summary>
	/// <param name="configuration">Конфигурация приложения.</param>
	/// <exception cref="ArgumentNullException">Конфигурация не задана.</exception>
	/// <exception cref="InvalidOperationException">Значение <see cref="DetailKey"/> не распознано.</exception>
	public static TracingOptions Read(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		return new TracingOptions
		{
			Detail = ReadDetail(configuration),
			Dir = ReadDirectory(configuration),
			MaxFileBytes = ReadMaxFileBytes(configuration),
			KeepFiles = ReadKeepFiles(configuration),
			OtlpEndpoint = ReadOtlpEndpoint(configuration),
		};
	}

	#region Вспомогательные методы

	/// <summary>
	/// Читает уровень детализации; пустое или пробельное значение — дефолт
	/// detailed, неизвестное значение — исключение с перечислением допустимых.
	/// Сравнение явное, а не через Enum.TryParse: числовые строки не должны
	/// проскакивать как «известные» значения.
	/// Traceability: openspec:ops/agent-tracing#requirement-trace-detail-levels
	/// </summary>
	private static TracingDetail ReadDetail(IConfiguration configuration)
	{
		var raw = configuration[DetailKey];
		if (string.IsNullOrWhiteSpace(raw))
		{
			return TracingDetail.Detailed;
		}

		var normalized = raw.Trim();
		if (string.Equals(normalized, "basic", StringComparison.OrdinalIgnoreCase))
		{
			return TracingDetail.Basic;
		}

		if (string.Equals(normalized, "detailed", StringComparison.OrdinalIgnoreCase))
		{
			return TracingDetail.Detailed;
		}

		throw new InvalidOperationException(
			$"Неизвестное значение {DetailKey}: «{raw}». Допустимые значения: basic, detailed.");
	}

	/// <summary>Читает каталог трасс; пустое или пробельное значение — дефолт.</summary>
	private static string ReadDirectory(IConfiguration configuration)
	{
		var raw = configuration[DirectoryKey];
		return string.IsNullOrWhiteSpace(raw) ? DefaultDirectory : raw.Trim();
	}

	/// <summary>Читает предел размера файла; нечисловое или неположительное значение — дефолт.</summary>
	private static long ReadMaxFileBytes(IConfiguration configuration)
	{
		var raw = configuration[MaxFileBytesKey];
		if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) && limit > 0)
		{
			return limit;
		}

		return DefaultMaxFileBytes;
	}

	/// <summary>Читает число хранимых архивов; нечисловое или отрицательное значение — дефолт.</summary>
	private static int ReadKeepFiles(IConfiguration configuration)
	{
		var raw = configuration[KeepFilesKey];
		if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keep) && keep >= 0)
		{
			return keep;
		}

		return DefaultKeepFiles;
	}

	/// <summary>Читает приёмник OTLP; пустое или пробельное значение — выключен.</summary>
	private static string? ReadOtlpEndpoint(IConfiguration configuration)
	{
		var raw = configuration[OtlpEndpointKey];
		return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
	}

	#endregion
}
