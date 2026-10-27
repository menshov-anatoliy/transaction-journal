namespace TransactionJournal.Infrastructure.Tracing;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenTelemetry;

/// <summary>
/// Локальный приёмник операционной трассы агентского харнеса: плоский JSONL,
/// одна строка — один спан, самодостаточная для grep (поле <c>service</c>
/// присутствует в каждой строке). Имена атрибутов сохраняются оригинальными
/// — с точками (<c>gen_ai.*</c>, <c>tj.*</c>); события пишутся массивом
/// объектов <c>{ts, name, attrs}</c>. Файл <c>traces.jsonl</c> создаётся лениво
/// в каталоге трасс, запись ведётся дозаписью через долгоживущий поток; при
/// превышении предела размера выполняется ротация в архив
/// <c>traces-{timestamp}.jsonl</c> с хранением ограниченного числа архивов.
/// Сбой записи деградирует в диагностику: исключение наружу не бросается,
/// экспортёр остаётся работоспособным — трассировка не может ломать журнал.
/// Traceability: openspec:ops/agent-tracing#requirement-trace-jsonl-sink
/// </summary>
public sealed class TraceJsonlExporter : BaseExporter<Activity>
{
	/// <summary>Имя основного файла трасс.</summary>
	public const string MainFileName = "traces.jsonl";

	/// <summary>Шаблон поиска архивов трасс.</summary>
	public const string ArchiveSearchPattern = "traces-*.jsonl";

	/// <summary>Формат метки времени в имени архива — однозначный и сортируемый.</summary>
	private const string ArchiveTimestampFormat = "yyyyMMdd-HHmmss-fff";

	private readonly string _directory;
	private readonly string _service;
	private readonly long _maxFileBytes;
	private readonly int _keepFiles;
	private readonly TimeProvider _timeProvider;
	private readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
	private readonly object _writeLock = new();

	/// <summary>Лениво создаваемый долгоживущий поток записи; <c>null</c> — поток не открыт.</summary>
	private StreamWriter? _writer;

	/// <summary>Текущий размер файла трасс в байтах, известный экспортёру.</summary>
	private long _currentFileBytes;

	/// <summary>
	/// Создаёт экспортёр над каталогом трасс из опций; относительный путь
	/// каталога должен быть разрешён вызывающей стороной от корня содержимого.
	/// </summary>
	/// <param name="options">Параметры журнала трасс: каталог, предел размера, число архивов.</param>
	/// <param name="service">Имя сервиса, записываемое в каждую строку.</param>
	/// <param name="timeProvider">Поставщик времени для метки архива; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Опции или имя сервиса не заданы.</exception>
	public TraceJsonlExporter(TracingOptions options, string service, TimeProvider? timeProvider = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentException.ThrowIfNullOrWhiteSpace(service);
		_directory = options.Dir;
		_service = service;
		_maxFileBytes = options.MaxFileBytes;
		_keepFiles = options.KeepFiles;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <summary>Полный путь основного файла трасс.</summary>
	private string MainFilePath => Path.Combine(_directory, MainFileName);

	/// <summary>
	/// Записывает партию спанов в файл трасс; каждая строка обрабатывается
	/// независимо, сбой строки деградирует в диагностику и не останавливает
	/// обработку остальных строк партии.
	/// Traceability: openspec:ops/agent-tracing#requirement-trace-jsonl-sink
	/// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-write-failure-degrades
	/// </summary>
	/// <param name="batch">Партия завершённых спанов.</param>
	/// <returns>Успех, если не было сбоев записи; иначе — отказ партии.</returns>
	public override ExportResult Export(in Batch<Activity> batch)
	{
		var failures = 0;
		lock (_writeLock)
		{
			foreach (var activity in batch)
			{
				if (activity is null)
				{
					continue;
				}

				try
				{
					WriteLine(SerializeSpan(activity));
				}
				catch (Exception failure)
				{
					// Сбой записи (занятый файл, полный диск, недоступный каталог)
					// не должен ломать работу агента: фиксируем диагностику,
					// сбрасываем поток и продолжаем со следующей строки.
					failures++;
					ReportWriteFailure(failure);
					ResetWriterAfterFailure();
				}
			}

			try
			{
				_writer?.Flush();
			}
			catch (Exception failure)
			{
				failures++;
				ReportWriteFailure(failure);
				ResetWriterAfterFailure();
			}
		}

		return failures == 0 ? ExportResult.Success : ExportResult.Failure;
	}

	/// <inheritdoc cref="BaseExporter{T}.OnForceFlush" />
	protected override bool OnForceFlush(int timeoutMilliseconds)
	{
		lock (_writeLock)
		{
			try
			{
				_writer?.Flush();
				return true;
			}
			catch (Exception failure)
			{
				ReportWriteFailure(failure);
				return false;
			}
		}
	}

	/// <inheritdoc cref="BaseExporter{T}.OnShutdown" />
	protected override bool OnShutdown(int timeoutMilliseconds)
	{
		lock (_writeLock)
		{
			try
			{
				_writer?.Flush();
				_writer?.Dispose();
				return true;
			}
			catch (Exception failure)
			{
				ReportWriteFailure(failure);
				return false;
			}
			finally
			{
				_writer = null;
			}
		}
	}

	#region Вспомогательные методы

	/// <summary>
	/// Формирует строку JSONL спана: все поля самодостаточны в строке,
	/// включая имя сервиса; имена атрибутов и событий — оригинальные.
	/// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-line-greppable
	/// </summary>
	private string SerializeSpan(Activity activity)
	{
		var record = new
		{
			ts = activity.StartTimeUtc.ToString("O", CultureInfo.InvariantCulture),
			service = _service,
			source = activity.Source.Name,
			name = activity.DisplayName,
			kind = activity.Kind.ToString(),
			trace_id = activity.TraceId.ToString(),
			span_id = activity.SpanId.ToString(),
			parent_span_id = activity.ParentSpanId.Equals(default) ? null : activity.ParentSpanId.ToString(),
			duration_ms = activity.Duration.TotalMilliseconds,
			status = FormatStatus(activity.Status),
			status_desc = activity.StatusDescription,
			attrs = FormatAttributes(activity.TagObjects),
			events = activity.Events.Select(FormatEvent).ToArray(),
		};
		return JsonSerializer.Serialize(record);
	}

	/// <summary>Переводит код статуса спана в строку формата OTel.</summary>
	private static string FormatStatus(ActivityStatusCode status) => status switch
	{
		ActivityStatusCode.Ok => "ok",
		ActivityStatusCode.Error => "error",
		_ => "unset",
	};

	/// <summary>
	/// Собирает атрибуты словарём с оригинальными именами ключей — точки
	/// в <c>gen_ai.*</c> и <c>tj.*</c> сохраняются, значения приводятся
	/// к JSON-скалярам.
	/// </summary>
	private static Dictionary<string, object?> FormatAttributes(IEnumerable<KeyValuePair<string, object?>> tags)
	{
		var attributes = new Dictionary<string, object?>();
		foreach (var tag in tags)
		{
			attributes[tag.Key] = NormalizeValue(tag.Value);
		}

		return attributes;
	}

	/// <summary>Приводит значение атрибута к сериализуемому виду: примитивы как есть, прочее — строкой.</summary>
	private static object? NormalizeValue(object? value) => value switch
	{
		null => null,
		string or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
		Array array => array.Cast<object?>().Select(NormalizeValue).ToArray(),
		_ => value.ToString(),
	};

	/// <summary>Формирует объект события спана: момент, имя и атрибуты события.</summary>
	private static object FormatEvent(ActivityEvent activityEvent) => new
	{
		ts = activityEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture),
		name = activityEvent.Name,
		attrs = FormatAttributes(activityEvent.Tags),
	};

	/// <summary>
	/// Дописывает строку в файл трасс, при необходимости предварительно
	/// выполнив ротацию; размер строки учитывается в байтах кодировки файла.
	/// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-rotation
	/// </summary>
	private void WriteLine(string line)
	{
		EnsureWriter();
		var lineBytes = _encoding.GetByteCount(line) + 1;
		if (_currentFileBytes > 0 && _currentFileBytes + lineBytes > _maxFileBytes)
		{
			RotateFile();
			EnsureWriter();
		}

		_writer!.Write(line);
		_writer!.Write('\n');
		_currentFileBytes += lineBytes;
	}

	/// <summary>Открывает поток дозаписи лениво и восстанавливает известный размер файла.</summary>
	private void EnsureWriter()
	{
		if (_writer is not null)
		{
			return;
		}

		Directory.CreateDirectory(_directory);
		_writer = new StreamWriter(MainFilePath, append: true, _encoding);
		_currentFileBytes = new FileInfo(MainFilePath).Length;
	}

	/// <summary>
	/// Ротация: текущий файл закрывается и переименовывается в архив с
	/// однозначной меткой времени, старейшие архивы сверх лимита удаляются,
	/// запись продолжается в новый <c>traces.jsonl</c>.
	/// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-rotation
	/// </summary>
	private void RotateFile()
	{
		CloseWriter();

		// Коллизия метки в ту же миллисекунду разрешается суффикс-счётчиком:
		// переименование не должно отказывать из-за занятого имени архива.
		var stamp = _timeProvider.GetLocalNow().ToString(ArchiveTimestampFormat, CultureInfo.InvariantCulture);
		var archivePath = Path.Combine(_directory, $"traces-{stamp}.jsonl");
		var counter = 2;
		while (File.Exists(archivePath))
		{
			archivePath = Path.Combine(_directory, $"traces-{stamp}-{counter}.jsonl");
			counter++;
		}

		File.Move(MainFilePath, archivePath);
		_currentFileBytes = 0;
		DeleteExcessArchives();
	}

	/// <summary>
	/// Удаляет старейшие архивы сверх лимита хранения «лучшие усилия»:
	/// неудача удаления отдельного архива становится диагностикой и не
	/// мешает продолжению записи.
	/// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-rotation
	/// </summary>
	private void DeleteExcessArchives()
	{
		var archivePaths = Directory
			.EnumerateFiles(_directory, ArchiveSearchPattern)
			.OrderBy(File.GetLastWriteTimeUtc)
			.ToList();

		var excess = archivePaths.Count - Math.Max(_keepFiles, 0);
		for (var index = 0; index < excess; index++)
		{
			try
			{
				File.Delete(archivePaths[index]);
			}
			catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException)
			{
				Trace.TraceWarning(
					"Не удалось удалить старый архив трасс «{0}»: {1}", archivePaths[index], failure.Message);
			}
		}
	}

	/// <summary>Закрывает текущий поток записи; сам поток файла сохраняется для переименования.</summary>
	private void CloseWriter()
	{
		_writer?.Dispose();
		_writer = null;
	}

	/// <summary>Сбрасывает поток после сбоя: следующая запись откроет файл лениво заново.</summary>
	private void ResetWriterAfterFailure()
	{
		try
		{
			_writer?.Dispose();
		}
		catch (Exception disposeFailure)
		{
			Trace.TraceWarning("Не удалось закрыть поток файла трасс после сбоя: {0}", disposeFailure.Message);
		}
		finally
		{
			_writer = null;
		}
	}

	/// <summary>Фиксирует сбой записи в диагностике без исключения наружу.</summary>
	private void ReportWriteFailure(Exception failure)
	{
		Trace.TraceError("Не удалось записать строку трассы в «{0}»: {1}", MainFilePath, failure);
	}

	#endregion
}
