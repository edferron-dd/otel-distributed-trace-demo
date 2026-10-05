using Azure.Messaging.ServiceBus;
using Subscriber.Models;

namespace Subscriber.Services;

public class QueueReaderService(
    ServiceBusClient client,
    QueueMessageStore queueMessageStore,
    IConfiguration configuration,
    ILogger<QueueReaderService> logger)
{
    private readonly string _queueName = configuration["ServiceBus:QueueName"] ?? "demo-q";

    public async Task<int> ReadMessagesAsync(int maxMessages = 20)
    {
        await using var receiver = client.CreateReceiver(_queueName);

        var messages = await receiver.ReceiveMessagesAsync(maxMessages, maxWaitTime: TimeSpan.FromSeconds(5));

        int count = 0;
        foreach (var msg in messages)
        {
            try
            {
                var received = new ReceivedMessage
                {
                    MessageId = msg.MessageId,
                    Body = msg.Body.ToString(),
                    Subject = msg.Subject,
                    ContentType = msg.ContentType,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    SequenceNumber = msg.SequenceNumber,
                    EnqueuedTimeUtc = msg.EnqueuedTime.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                queueMessageStore.AddMessage(received);
                await receiver.CompleteMessageAsync(msg);
                count++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing queue message {MessageId}", msg.MessageId);
                await receiver.AbandonMessageAsync(msg);
            }
        }

        logger.LogInformation("Read {Count} messages from queue '{Queue}'", count, _queueName);
        return count;
    }
}
