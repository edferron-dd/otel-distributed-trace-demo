using System.Diagnostics;
using ServiceBusTelemetry;

// Dependency-free regression checks: dotnet run --project tests/ServiceBusTelemetry.Tests
// Use real activities, with destination tags added after start as the SDK does.
var scenarios = 0;
using var listener = new ActivityListener
{
    ShouldListenTo = _ => true,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = ServiceBusPeerService.Enrich
};
ActivitySource.AddActivityListener(listener);

foreach (var destination in new[] { "demo-q", "demo-topic" })
{
    foreach (var kind in new[] { ActivityKind.Client, ActivityKind.Producer, ActivityKind.Consumer })
    {
        foreach (var key in new[] { "messaging.destination.name", "messaging.destination", "peer.messaging.destination" })
        {
            CheckDestination("Azure.Messaging.ServiceBus.ServiceBusSender", kind, key, destination, destination);
        }
    }
}

CheckDestination("Azure.Messaging.ServiceBus.ServiceBusReceiver", ActivityKind.Consumer,
    "messaging.destination.name", "demo-topic/Subscriptions/subscriber-subscription", "demo-topic");
CheckDestination("Azure.Messaging.ServiceBus.ServiceBusReceiver", ActivityKind.Consumer,
    "messaging.destination", "demo-topic/subscriptions/subscriber-subscription", "demo-topic");
CheckDestination("Azure.Messaging.ServiceBus", ActivityKind.Producer,
    "messaging.destination.name", "custom-topic", "custom-topic");

// The current destination key takes precedence if an SDK supplies both versions.
using (var source = new ActivitySource("Azure.Messaging.ServiceBus.ServiceBusSender"))
using (var activity = source.StartActivity("publish", ActivityKind.Producer)!)
{
    activity.SetTag("messaging.destination.name", "demo-topic");
    activity.SetTag("messaging.destination", "demo-q");
    activity.Stop();
    AssertEqual("demo-topic", activity.GetTagItem("peer.service"), "destination key precedence");
    scenarios++;
}

// Do not retag HTTP, other SDKs, lookalike sources, or internal/server spans.
CheckUnchanged("System.Net.Http", ActivityKind.Client, "demo-q");
CheckUnchanged("Azure.Storage.Blobs", ActivityKind.Client, "demo-q");
CheckUnchanged("Azure.Messaging.ServiceBusOther", ActivityKind.Producer, "demo-q");
CheckUnchanged("Publisher", ActivityKind.Producer, "demo-q");
CheckUnchanged("Azure.Messaging.ServiceBus.ServiceBusSender", ActivityKind.Internal, "demo-q");
CheckUnchanged("Azure.Messaging.ServiceBus.ServiceBusReceiver", ActivityKind.Server, "demo-q");

// A missing destination must not fall back to the namespace or erase a peer.
CheckUnchanged("Azure.Messaging.ServiceBus.ServiceBusSender", ActivityKind.Client, null);
CheckUnchanged("Azure.Messaging.ServiceBus.ServiceBusSender", ActivityKind.Producer, " ");
using (var source = new ActivitySource("Azure.Messaging.ServiceBus.ServiceBusSender"))
using (var activity = source.StartActivity("publish", ActivityKind.Producer)!)
{
    activity.SetTag("server.address", "central-dd-demo.servicebus.windows.net");
    activity.Stop();
    AssertEqual(null, activity.GetTagItem("peer.service"), "missing entity and peer");
    scenarios++;
}

Console.WriteLine($"Service Bus peer-service regression checks passed ({scenarios} scenarios).");

void CheckDestination(string sourceName, ActivityKind kind, string key, string destination, string peer)
{
    using var source = new ActivitySource(sourceName);
    var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
        ActivityTraceFlags.Recorded, "demo=value", isRemote: true);
    using var activity = source.StartActivity("servicebus.publish", kind, parent)!;
    activity.SetTag("peer.service", "subscriber");
    activity.SetTag("messaging.system", "servicebus");
    activity.SetTag(key, destination);
    activity.SetTag("server.address", "central-dd-demo.servicebus.windows.net");
    activity.SetTag("messaging.message.id", "message-123");
    activity.SetTag("messaging.destination.subscription.name", "subscriber-subscription");
    var spanId = activity.SpanId;
    var tags = activity.TagObjects.Where(tag => tag.Key != "peer.service").ToArray();

    AssertEqual("subscriber", activity.GetTagItem("peer.service"), "before span completion");
    activity.Stop();
    AssertEqual(peer, activity.GetTagItem("peer.service"), $"{kind} {key} {destination}");
    AssertEqual(parent.TraceId, activity.TraceId, "trace ID");
    AssertEqual(parent.SpanId, activity.ParentSpanId, "parent span ID");
    AssertEqual(spanId, activity.SpanId, "span ID");
    AssertEqual(parent.TraceState, activity.TraceStateString, "trace state");
    if (!tags.SequenceEqual(activity.TagObjects.Where(tag => tag.Key != "peer.service")))
        throw new InvalidOperationException("Existing messaging attributes changed.");
    scenarios++;
}

void CheckUnchanged(string sourceName, ActivityKind kind, string? destination)
{
    using var source = new ActivitySource(sourceName);
    using var activity = source.StartActivity("operation", kind)!;
    activity.SetTag("peer.service", "existing-peer");
    activity.SetTag("messaging.destination.name", destination);
    activity.SetTag("server.address", "central-dd-demo.servicebus.windows.net");
    activity.Stop();
    AssertEqual("existing-peer", activity.GetTagItem("peer.service"), $"unchanged {sourceName} {kind}");
    scenarios++;
}

static void AssertEqual(object? expected, object? actual, string description)
{
    if (!Equals(expected, actual))
        throw new InvalidOperationException($"{description}: expected '{expected}', got '{actual}'.");
}
