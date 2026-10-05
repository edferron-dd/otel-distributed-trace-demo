using Microsoft.AspNetCore.Mvc;
using Subscriber.Models;
using Subscriber.Services;

namespace Subscriber.Controllers;

public class HomeController(
    MessageStore messageStore,
    QueueMessageStore queueMessageStore,
    QueueReaderService queueReaderService) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var vm = new SubscriberViewModel
        {
            TopicMessages = messageStore.GetMessages(),
            QueueMessages = queueMessageStore.GetMessages()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadQueue()
    {
        try
        {
            var count = await queueReaderService.ReadMessagesAsync(maxMessages: 20);
            TempData["QueueInfo"] = count > 0
                ? $"Read {count} message(s) from demo-q."
                : "No messages available in demo-q.";
        }
        catch (Exception ex)
        {
            TempData["QueueError"] = $"Failed to read from demo-q: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
