using BifitReport.Agent.Archiving;
using BifitReport.Agent.Configuration;
using BifitReport.Agent.Delivery;
using BifitReport.Agent.Infrastructure;
using BifitReport.Agent.Parsing;
using BifitReport.Agent.Queue;
using BifitReport.Agent.Reporting;
using BifitReport.Agent.State;
using BifitReport.Agent.Tailing;
using BifitReport.Agent.Workers;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "BIFIT Report");
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "BIFIT_REPORT_");

builder.Services.AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<DeliveryOptions>()
    .Bind(builder.Configuration.GetSection(DeliveryOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<VkOptions>()
    .Bind(builder.Configuration.GetSection(VkOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<ReportOptions>()
    .Bind(builder.Configuration.GetSection(ReportOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<ArchiveOptions>()
    .Bind(builder.Configuration.GetSection(ArchiveOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<AgentOptions>, ConfigurationValidator>();
builder.Services.AddSingleton<IValidateOptions<DeliveryOptions>, ConfigurationValidator>();
builder.Services.AddSingleton<IValidateOptions<VkOptions>, ConfigurationValidator>();
builder.Services.AddSingleton<IValidateOptions<ReportOptions>, ConfigurationValidator>();
builder.Services.AddSingleton<IValidateOptions<ArchiveOptions>, ConfigurationValidator>();

builder.Services.AddSingleton<AtomicJsonFile>();
builder.Services.AddSingleton<AppPaths>();
builder.Services.AddSingleton<AgentHealthStore>();
builder.Services.AddSingleton<LogTailer>();
builder.Services.AddSingleton<SensitiveDataRedactor>();
builder.Services.AddSingleton<ErrorClassifier>();
builder.Services.AddSingleton<BifitLogParser>();
builder.Services.AddSingleton<ReportStateStore>();
builder.Services.AddSingleton<ReportFormatter>();
builder.Services.AddSingleton<DiskMessageQueue>();
builder.Services.AddSingleton<ReportAggregator>();
builder.Services.AddSingleton<VkTokenProvider>();
builder.Services.AddSingleton<VkApiClient>();
builder.Services.AddSingleton<LogArchiver>();

builder.Services.AddHostedService<LogIngestionWorker>();
builder.Services.AddHostedService<OutboundDeliveryWorker>();
builder.Services.AddHostedService<PeriodicReportWorker>();
builder.Services.AddHostedService<LogArchiveWorker>();

// Ошибка одного фонового обработчика не должна останавливать всю службу: иначе
// единственная проблема с диском или каталогом состояния оставляет кассу без
// наблюдения до ручного запуска (см. инцидент 15.11.2026, событие 1000 .NET Runtime).
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

await builder.Build().RunAsync();
