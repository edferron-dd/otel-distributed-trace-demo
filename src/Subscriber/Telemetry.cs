using System.Diagnostics;

namespace Subscriber;

/// <summary>
/// Central definitions for OpenTelemetry instrumentation in the Subscriber.
/// </summary>
public static class Telemetry
{
    /// <summary>Logical service name reported as the OTel resource.</summary>
    public const string ServiceName = "subscriber";

    public const string ServiceVersion = "1.0.0";

    /// <summary>Custom ActivitySource for the Service Bus trigger spans.</summary>
    public const string ActivitySourceName = "Subscriber";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ServiceVersion);
}
