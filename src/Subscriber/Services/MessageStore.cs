using Subscriber.Models;
using System.Collections.Concurrent;

namespace Subscriber.Services;

public class MessageStore
{
    private readonly ConcurrentQueue<ReceivedMessage> _messages = new();
    private const int MaxMessages = 100;

    public void AddMessage(ReceivedMessage message)
    {
        _messages.Enqueue(message);

        // Trim to max capacity
        while (_messages.Count > MaxMessages)
        {
            _messages.TryDequeue(out _);
        }
    }

    public IReadOnlyList<ReceivedMessage> GetMessages()
    {
        return [.. _messages.Reverse()];
    }

    public int Count => _messages.Count;
}
