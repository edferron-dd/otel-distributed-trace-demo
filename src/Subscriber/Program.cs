using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Logs;
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
        // OTEL_EXPORTER_OTLP_* environment variables.
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: Telemetry.ServiceName,
                serviceVersion: Telemetry.ServiceVersion))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.ActivitySourceName)
                .AddSource("Azure.Messaging.ServiceBus")
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithLogging(logging => logging.AddOtlpExporter())
            // Correlates worker spans/logs with the Functions host invocation.
            .UseFunctionsWorkerDefaults();
    })
    .Build();

host.Run();
