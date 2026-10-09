using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Subscriber;
using Subscriber.Services;

// Opt the Azure SDK into emitting OpenTelemetry ActivitySource spans.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // In-memory stores shared across trigger invocations and the HTTP viewer
        // (singletons live for the lifetime of the Functions host instance).
        services.AddSingleton<MessageStore>();
        services.AddSingleton<QueueMessageStore>();

        // --- OpenTelemetry (native SDK, OTLP exporter) ---
        // Exporter endpoint/headers/protocol are read from the standard
        // OTEL_EXPORTER_OTLP_* environment variables. UseOtlpExporter() is required
        // for the signal-specific OTEL_EXPORTER_OTLP_{TRACES,LOGS}_* variables; the
        // per-signal AddOtlpExporter() only honors the general ones.
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: Telemetry.ServiceName,
                serviceVersion: Telemetry.ServiceVersion)
                .AddAttributes(Telemetry.GitResourceAttributes()))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.ActivitySourceName)
                .AddSource("Azure.Messaging.ServiceBus.*")
                .AddHttpClientInstrumentation())
            .WithLogging()
            .UseOtlpExporter()
            // Correlates worker spans/logs with the Functions host invocation.
            .UseFunctionsWorkerDefaults()
            // UseFunctionsWorkerDefaults() sets deployment.environment.name from
            // WEBSITE_SLOT_NAME ("production"); re-apply OTEL_RESOURCE_ATTRIBUTES
            // afterwards so the configured values take precedence.
            .ConfigureResource(resource => resource.AddEnvironmentVariableDetector());
    })
    .Build();

host.Run();
