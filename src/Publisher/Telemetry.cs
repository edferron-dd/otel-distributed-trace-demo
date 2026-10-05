using System.Diagnostics;

namespace Publisher;

/// <summary>
/// Central definitions for OpenTelemetry instrumentation in the Publisher.
/// </summary>
public static class Telemetry
{
    /// <summary>Logical service name reported as the OTel resource.</summary>
    public const string ServiceName = "publisher";

    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// Custom ActivitySource for app-level spans. Registered with the tracer
    /// provider via <c>AddSource(Telemetry.ActivitySourceName)</c>.
    /// </summary>
    public const string ActivitySourceName = "Publisher";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ServiceVersion);
}
