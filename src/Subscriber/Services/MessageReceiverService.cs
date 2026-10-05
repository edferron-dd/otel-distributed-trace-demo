using Azure.Messaging.ServiceBus;
using Subscriber.Models;

namespace Subscriber.Services;

public class MessageReceiverService(
    ServiceBusClient client,
    MessageStore messageStore,
    IConfiguration configuration,
    ILogger<MessageReceiverService> logger) : BackgroundService
{
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topicName = configuration["ServiceBus:TopicName"] ?? "demo-topic";
        var subscriptionName = configuration["ServiceBus:SubscriptionName"] ?? "subscriber-subscription";

        var options = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 5,
            AutoCompleteMessages = false,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
        };

        _processor = client.CreateProcessor(topicName, subscriptionName, options);
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        logger.LogInformation(
            "Starting Service Bus processor for topic '{Topic}', subscription '{Subscription}'",
            topicName, subscriptionName);

        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        finally
        {
            await _processor.StopProcessingAsync();
            await _processor.DisposeAsync();
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = args.Message.Body.ToString();
            logger.LogInformation("Received message: {MessageId}", args.Message.MessageId);

            var received = new ReceivedMessage
            {
                MessageId = args.Message.MessageId,
                Body = body,
                Subject = args.Message.Subject,
                ContentType = args.Message.ContentType,
                ReceivedAt = DateTimeOffset.UtcNow,
                SequenceNumber = args.Message.SequenceNumber,
                EnqueuedTimeUtc = args.Message.EnqueuedTime.ToString("yyyy-MM-dd HH:mm:ss UTC")
            };

            messageStore.AddMessage(received);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing message {MessageId}", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Service Bus error. Source: {ErrorSource}, Entity: {EntityPath}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

}
