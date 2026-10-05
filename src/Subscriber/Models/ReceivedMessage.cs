namespace Subscriber.Models;

public class ReceivedMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string? ContentType { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public long SequenceNumber { get; set; }
    public string EnqueuedTimeUtc { get; set; } = string.Empty;
}
