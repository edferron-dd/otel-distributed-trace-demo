namespace Subscriber.Models;

public class SubscriberViewModel
{
    public IReadOnlyList<ReceivedMessage> TopicMessages { get; set; } = [];
    public IReadOnlyList<ReceivedMessage> QueueMessages { get; set; } = [];
}
