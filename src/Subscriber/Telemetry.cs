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

    /// <summary>
    /// Source revision as the resource attributes Datadog Source Code Integration
    /// reads for OpenTelemetry data. Values come from DD_GIT_COMMIT_SHA and
    /// DD_GIT_REPOSITORY_URL, which the deploy workflow sets on each deploy; the
    /// OTel SDK doesn't read DD_* variables itself.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, object>> GitResourceAttributes()
    {
        var commitSha = Environment.GetEnvironmentVariable("DD_GIT_COMMIT_SHA");
        var repositoryUrl = Environment.GetEnvironmentVariable("DD_GIT_REPOSITORY_URL");

        if (!string.IsNullOrEmpty(commitSha))
            yield return new("git.commit.sha", commitSha);
        if (!string.IsNullOrEmpty(repositoryUrl))
            yield return new("git.repository_url", repositoryUrl);
    }
}
