using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Subscriber;

/// <summary>
/// Emits a "queue hop" span between the Publisher's producer span and the
/// Subscriber's consumer span, reported under a service named after the queue or
/// topic (demo-q, demo-topic). Datadog's flow map draws one edge per cross-service
/// parent→child span, so this is what puts the entity between the two services:
/// publisher → demo-q → subscriber.
/// </summary>
/// <remarks>
/// service.name is a resource attribute, fixed per TracerProvider, so each entity
/// gets its own provider and ActivitySource. Their sources must not be added to the
/// Subscriber's provider, or the hop spans would be reported as "subscriber".
/// Registered as a singleton so the host disposes (and flushes) the providers.
/// </remarks>
public sealed class QueueHopTelemetry : IDisposable
{
    private const string SourcePrefix = "ServiceBus.Hop.";

    private readonly Dictionary<string, (ActivitySource Source, TracerProvider Provider)> _hops = [];
    private readonly string? _namespace;

    public QueueHopTelemetry(IConfiguration configuration, IEnumerable<string> entities)
    {
        _namespace = configuration["ServiceBusConnection:fullyQualifiedNamespace"];

        foreach (var entity in entities)
        {
            var sourceName = SourcePrefix + entity;
            var provider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateEmpty()
                    .AddTelemetrySdk()
                    // OTEL_RESOURCE_ATTRIBUTES carries deployment.environment.name
                    // (env:demo). AddService comes after it so OTEL_SERVICE_NAME
                    // ("subscriber") can't override the entity's service name.
                    .AddEnvironmentVariableDetector()
                    .AddService(entity)
                    .AddAttributes([new("messaging.system", "servicebus")]))
                .AddSource(sourceName)
                .AddOtlpExporter(options => ConfigureExporter(options, configuration))
                .Build();

            _hops[entity] = (new ActivitySource(sourceName), provider);
        }
    }

    /// <summary>
    /// Records the time the message spent in <paramref name="entity"/>, from enqueue
    /// to now, as a child of the Publisher's span. Returns the context the consumer
    /// span should use as its parent, or <paramref name="parentContext"/> if the
    /// message carried no trace context or the entity isn't registered.
    /// </summary>
    public ActivityContext RecordHop(
        ServiceBusReceivedMessage message, string entity, string? subscription, ActivityContext parentContext)
    {
        if (parentContext == default || !_hops.TryGetValue(entity, out var hop))
            return parentContext;

        using var activity = hop.Source.StartActivity(
            entity, ActivityKind.Consumer, parentContext, startTime: message.EnqueuedTime);

        if (activity is null)
            return parentContext;

        activity.SetTag("operation.name", "servicebus.queue");
        activity.SetTag("resource.name", entity);
        activity.SetTag("messaging.system", "servicebus");
        activity.SetTag("messaging.destination.name", entity);
        activity.SetTag("messaging.destination.subscription.name", subscription);
        activity.SetTag("messaging.message.id", message.MessageId);
        activity.SetTag("server.address", _namespace);

        // Ends here, so the span covers only the wait in the queue; processing is
        // the consumer span's job.
        return activity.Context;
    }

    public void Dispose()
    {
        foreach (var (source, provider) in _hops.Values)
        {
            provider.Dispose();
            source.Dispose();
        }
    }

    // A standalone provider isn't covered by UseOtlpExporter(), and AddOtlpExporter()
    // only honors the general OTEL_EXPORTER_OTLP_* variables. Read the traces-specific
    // settings the Subscriber already uses, so hop spans reach the same intake.
    private static void ConfigureExporter(OtlpExporterOptions options, IConfiguration configuration)
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"];
        var headers = configuration["OTEL_EXPORTER_OTLP_TRACES_HEADERS"]
                      ?? configuration["OTEL_EXPORTER_OTLP_HEADERS"];
        var protocol = configuration["OTEL_EXPORTER_OTLP_TRACES_PROTOCOL"]
                       ?? configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];

        if (!string.IsNullOrEmpty(endpoint))
            options.Endpoint = new Uri(endpoint);
        if (!string.IsNullOrEmpty(headers))
            options.Headers = headers;
        if (protocol == "http/protobuf")
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
    }
}
