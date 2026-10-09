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
