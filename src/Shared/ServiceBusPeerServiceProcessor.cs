using System.Diagnostics;
using OpenTelemetry;

namespace ServiceBusTelemetry;

internal sealed class ServiceBusPeerServiceProcessor : BaseProcessor<Activity>
{
    // SDK spans can receive destination tags after they start. Enrich on end,
    // before the OTLP exporter reads the completed activity.
    public override void OnEnd(Activity activity) => ServiceBusPeerService.Enrich(activity);
}
