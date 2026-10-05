using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace Subscriber.Functions;

/// <summary>
/// Extracts W3C trace context that the Publisher's Service Bus client injected
/// into each message, so the consumer span continues the same distributed trace.
/// </summary>
internal static class TraceContext
{
    public static ActivityContext Extract(ServiceBusReceivedMessage message)
    {
        // Newer Azure SDKs write "traceparent"; older ones write "Diagnostic-Id".
        // Both hold a W3C traceparent-formatted value.
        var traceParent = GetString(message, "traceparent")
                          ?? GetString(message, "Diagnostic-Id");
        var traceState = GetString(message, "tracestate");

        if (!string.IsNullOrEmpty(traceParent)
            && ActivityContext.TryParse(traceParent, traceState, out var context))
        {
            return context;
        }

        return default;
    }

    private static string? GetString(ServiceBusReceivedMessage message, string key)
        => message.ApplicationProperties.TryGetValue(key, out var value)
            ? value as string
            : null;
}
