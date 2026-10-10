using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using Publisher.Models;

namespace Publisher.Services;

public class MessagePublisherService
{
    private readonly ServiceBusClient _client;
    private readonly string _topicName;
    private readonly string _queueName;
    private readonly List<SentMessage> _sentMessages = [];
    private readonly List<SentMessage> _sentQueueMessages = [];
    private readonly object _lock = new();

    public MessagePublisherService(ServiceBusClient client, IConfiguration configuration)
    {
        _client = client;
        _topicName = configuration["ServiceBus:TopicName"] ?? "demo-topic";
        _queueName = configuration["ServiceBus:QueueName"] ?? "demo-q";
    }

    public async Task<SentMessage> SendMessageAsync(string body, string? subject = null)
    {
        await using var sender = _client.CreateSender(_topicName);
        return await SendAndRecordAsync(sender, body, subject, _sentMessages);
    }

    public async Task<SentMessage> SendToQueueAsync(string body, string? subject = null)
    {
        await using var sender = _client.CreateSender(_queueName);
        return await SendAndRecordAsync(sender, body, subject, _sentQueueMessages);
    }

    private async Task<SentMessage> SendAndRecordAsync(
        ServiceBusSender sender, string body, string? subject, List<SentMessage> store)
    {
        // App-level span that becomes the parent of the Service Bus SDK's
        // "publish" span, so the whole send shows up as one trace. It is Internal
        // and carries no messaging.destination.* tags: the SDK's own producer
        // spans represent the send. A Producer span here would make Datadog infer
        // a demo-q/demo-topic node between it and the SDK spans beneath it,
        // showing the Publisher on both sides of the queue.
        using var activity = Telemetry.ActivitySource.StartActivity(
            $"publish {sender.EntityPath}", ActivityKind.Internal);
        activity?.SetTag("peer.service", sender.EntityPath);

        var message = new ServiceBusMessage(body)
        {
            Subject = subject,
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "text/plain"
        };

        activity?.SetTag("messaging.message.id", message.MessageId);

        await sender.SendMessageAsync(message);

        var sent = new SentMessage
        {
            Body = body,
            Subject = subject,
            SentAt = DateTimeOffset.UtcNow,
            MessageId = message.MessageId
        };

        lock (_lock)
        {
            store.Insert(0, sent);
            if (store.Count > 50)
                store.RemoveAt(store.Count - 1);
        }

        return sent;
    }

    public IReadOnlyList<SentMessage> GetSentMessages()
    {
        lock (_lock) { return _sentMessages.ToList(); }
    }

    public IReadOnlyList<SentMessage> GetSentQueueMessages()
    {
        lock (_lock) { return _sentQueueMessages.ToList(); }
    }
}
