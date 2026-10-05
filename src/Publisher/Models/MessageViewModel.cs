namespace Publisher.Models;

public class MessageViewModel
{
    public string? MessageBody { get; set; }
    public string? Subject { get; set; }
    public string? QueueMessageBody { get; set; }
    public string? QueueSubject { get; set; }
    public List<SentMessage> SentMessages { get; set; } = [];
    public List<SentMessage> SentQueueMessages { get; set; } = [];
}

public class SentMessage
{
    public string Body { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string MessageId { get; set; } = string.Empty;
}
