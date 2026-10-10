using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Subscriber.Models;
using Subscriber.Services;

namespace Subscriber.Functions;

/// <summary>
/// Service Bus triggers. Each fires when a new message arrives, records it in the
/// in-memory store (viewable via <see cref="MessageViewerFunction"/>), and emits a
/// consumer span linked to the Publisher's trace via the message's trace context.
/// </summary>
public class ServiceBusTriggerFunctions(
    MessageStore messageStore,
    QueueMessageStore queueMessageStore,
    IConfiguration configuration,
    ILogger<ServiceBusTriggerFunctions> logger)
{
    private readonly string? _namespace =
        configuration["ServiceBusConnection:fullyQualifiedNamespace"];

    // Entity names are fixed for this demo. (The %...%% app-setting token syntax
    // can't be used here because ServiceBus__TopicName is parsed by .NET config
    // into the nested key ServiceBus:TopicName, which the WebJobs name resolver
    // won't find — that disables the trigger at indexing time.)
    private const string TopicName = "demo-topic";
    private const string SubscriptionName = "subscriber-subscription";
    private const string QueueName = "demo-q";

    [Function(nameof(ProcessTopicMessage))]
    public void ProcessTopicMessage(
        [ServiceBusTrigger(
            topicName: TopicName,
            subscriptionName: SubscriptionName,
            Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message)
        => Handle(message, messageStore, TopicName, SubscriptionName);

    [Function(nameof(ProcessQueueMessage))]
    public void ProcessQueueMessage(
        [ServiceBusTrigger(
            queueName: QueueName,
            Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message)
        => Handle(message, queueMessageStore, QueueName, subscription: null);

    private void Handle(
        ServiceBusReceivedMessage message, IMessageStore store, string entity, string? subscription)
    {
        var parentContext = TraceContext.Extract(message);

        // The Functions worker's invocation span (Activity.Current here) starts its
        // own trace. Parent the consumer span on the Publisher's trace instead, and
        // link back to the invocation span so the two can still be navigated.
        var links = Activity.Current is { } invocation
            ? new[] { new ActivityLink(invocation.Context) }
            : null;

        using var activity = Telemetry.ActivitySource.StartActivity(
            $"process {entity}", ActivityKind.Consumer, parentContext, links: links);

        // Consumers report the entity in messaging.destination.name (same key the
        // Publisher's producer spans use), which is what links both services to the
        // same queue/topic node in the Datadog service map.
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("messaging.operation", "process");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", entity);
        activity?.SetTag("peer.service", entity);
        activity?.SetTag("messaging.destination.subscription.name", subscription);
        activity?.SetTag("server.address", _namespace);
        activity?.SetTag("messaging.message.id", message.MessageId);
        activity?.SetTag("messaging.servicebus.sequence_number", message.SequenceNumber);

        logger.LogInformation(
            "Received message {MessageId} from {Entity}", message.MessageId, entity);

        var received = new ReceivedMessage
        {
            MessageId = message.MessageId,
            Body = message.Body.ToString(),
            Subject = message.Subject,
            ContentType = message.ContentType,
            ReceivedAt = DateTimeOffset.UtcNow,
            SequenceNumber = message.SequenceNumber,
            EnqueuedTimeUtc = message.EnqueuedTime.ToString("yyyy-MM-dd HH:mm:ss UTC")
        };

        store.AddMessage(received);
    }
}
