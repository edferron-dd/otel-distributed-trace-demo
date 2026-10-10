using System.Diagnostics;

namespace ServiceBusTelemetry;

internal static class ServiceBusPeerService
{
    public static void Enrich(Activity activity)
    {
        if (activity.Source.Name != "Azure.Messaging.ServiceBus"
            && !activity.Source.Name.StartsWith("Azure.Messaging.ServiceBus.", StringComparison.Ordinal))
            return;

        if (activity.Kind is not (ActivityKind.Client or ActivityKind.Producer or ActivityKind.Consumer))
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
