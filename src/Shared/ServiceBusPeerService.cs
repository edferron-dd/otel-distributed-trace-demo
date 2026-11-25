using System.Diagnostics;

namespace ServiceBusTelemetry;

internal static class ServiceBusPeerService
{
    // Attributes Datadog reads as peer tags (directly or as OTel fallbacks for
    // peer.service, peer.hostname, peer.messaging.destination/system).
    internal static readonly string[] PeerTagSources =
    [
        "peer.service",
        "peer.hostname",
        "peer.messaging.destination",
        "peer.messaging.system",
        "messaging.destination.name",
        "messaging.destination",
        "messaging.system",
        "server.address",
        "net.peer.name",
    ];

    public static void Enrich(Activity activity)
    {
        if (activity.Source.Name != "Azure.Messaging.ServiceBus"
            && !activity.Source.Name.StartsWith("Azure.Messaging.ServiceBus.", StringComparison.Ordinal))
            return;

        // The SDK's client send span is a sibling of its producer span and never
        // has a cross-service child, so Datadog draws its peer tags as an
        // inferred demo-q/demo-topic node next to the instrumented one. The
        // producer and consumer spans already carry the destination, so strip
        // the client span's peer tags instead of tagging it.
        if (activity.Kind == ActivityKind.Client)
        {
            foreach (var key in PeerTagSources)
                activity.SetTag(key, null);
            return;
        }

        if (activity.Kind is not (ActivityKind.Producer or ActivityKind.Consumer))
            return;

        // Azure SDK versions use different destination keys. Keep those tags
        // intact and use the entity, rather than the namespace, as the peer.
        var destination = activity.GetTagItem("messaging.destination.name") as string
                          ?? activity.GetTagItem("messaging.destination") as string
                          ?? activity.GetTagItem("peer.messaging.destination") as string;

        if (string.IsNullOrWhiteSpace(destination))
            return;

        // A topic receiver's entity path can include its subscription. Both
        // senders and receivers must identify the topic as the same peer.
        var subscriptionIndex = destination.IndexOf("/Subscriptions/", StringComparison.OrdinalIgnoreCase);
        if (subscriptionIndex > 0)
            destination = destination[..subscriptionIndex];

        activity.SetTag("peer.service", destination);
    }
}
