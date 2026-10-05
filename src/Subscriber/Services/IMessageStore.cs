using Subscriber.Models;

namespace Subscriber.Services;

public interface IMessageStore
{
    void AddMessage(ReceivedMessage message);
    IReadOnlyList<ReceivedMessage> GetMessages();
    int Count { get; }
}
