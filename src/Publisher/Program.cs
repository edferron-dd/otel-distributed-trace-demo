using Azure.Identity;
using Azure.Messaging.ServiceBus;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Publisher;
using Publisher.Services;

// Opt the Azure SDK into emitting OpenTelemetry ActivitySource spans. This also
// makes the Service Bus client inject W3C trace context (traceparent) into each
// outgoing message, so the Subscriber can continue the same distributed trace.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// --- OpenTelemetry (native SDK, OTLP exporter) ---
// Exporter endpoint/headers/protocol are read from the standard
// OTEL_EXPORTER_OTLP_* environment variables.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: Telemetry.ServiceName,
        serviceVersion: Telemetry.ServiceVersion))
    .WithTracing(tracing => tracing
        .AddSource(Telemetry.ActivitySourceName)
        .AddSource("Azure.Messaging.ServiceBus")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

// Register Service Bus client - uses Managed Identity when deployed to Azure VM,
// falls back to connection string from appsettings for local development
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var fullyQualifiedNamespace = config["ServiceBus:FullyQualifiedNamespace"];
    var connectionString = config["ServiceBus:ConnectionString"];

    if (!string.IsNullOrEmpty(fullyQualifiedNamespace))
    {
        // Use Managed Identity (preferred for Azure-hosted VMs)
        return new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());
    }

    // Fall back to connection string
    return new ServiceBusClient(connectionString);
});

builder.Services.AddSingleton<MessagePublisherService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
