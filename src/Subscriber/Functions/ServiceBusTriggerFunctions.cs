using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
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
    ILogger<ServiceBusTriggerFunctions> logger)
{
    [Function(nameof(ProcessTopicMessage))]
    public void ProcessTopicMessage(
        [ServiceBusTrigger(
            topicName: "%ServiceBus__TopicName%",
            subscriptionName: "%ServiceBus__SubscriptionName%",
            Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message)
        => Handle(message, messageStore, "demo-topic");

    [Function(nameof(ProcessQueueMessage))]
    public void ProcessQueueMessage(
        [ServiceBusTrigger(
            queueName: "%ServiceBus__QueueName%",
            Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message)
        => Handle(message, queueMessageStore, "demo-q");

    private void Handle(ServiceBusReceivedMessage message, IMessageStore store, string entity)
    {
        var parentContext = TraceContext.Extract(message);

        using var activity = Telemetry.ActivitySource.StartActivity(
            $"process {entity}", ActivityKind.Consumer, parentContext);

        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("messaging.source.name", entity);
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
