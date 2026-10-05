using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Subscriber.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Register Service Bus client - uses Managed Identity when deployed to Azure VM,
// falls back to connection string from appsettings for local development
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var fullyQualifiedNamespace = config["ServiceBus:FullyQualifiedNamespace"];
    var connectionString = config["ServiceBus:ConnectionString"];

    if (!string.IsNullOrEmpty(fullyQualifiedNamespace))
    {
        return new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());
    }

    return new ServiceBusClient(connectionString);
});

builder.Services.AddSingleton<MessageStore>();
builder.Services.AddSingleton<QueueMessageStore>();
builder.Services.AddSingleton<QueueReaderService>();
builder.Services.AddHostedService<MessageReceiverService>();

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
