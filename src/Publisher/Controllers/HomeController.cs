using Microsoft.AspNetCore.Mvc;
using Publisher.Models;
using Publisher.Services;

namespace Publisher.Controllers;

public class HomeController(MessagePublisherService publisherService, ILogger<HomeController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var vm = new MessageViewModel
        {
            SentMessages = [.. publisherService.GetSentMessages()],
            SentQueueMessages = [.. publisherService.GetSentQueueMessages()]
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(MessageViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.MessageBody))
        {
            ModelState.AddModelError(nameof(model.MessageBody), "Message body is required.");
            model.SentMessages = [.. publisherService.GetSentMessages()];
            model.SentQueueMessages = [.. publisherService.GetSentQueueMessages()];
            return View("Index", model);
        }

        try
        {
            var sent = await publisherService.SendMessageAsync(model.MessageBody, model.Subject);
            logger.LogInformation("Message sent to topic: {MessageId}", sent.MessageId);
            TempData["Success"] = $"Message sent to demo-topic! ID: {sent.MessageId}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send message to topic");
            TempData["Error"] = $"Failed to send message: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToQueue(MessageViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.QueueMessageBody))
        {
            ModelState.AddModelError(nameof(model.QueueMessageBody), "Message body is required.");
            model.SentMessages = [.. publisherService.GetSentMessages()];
            model.SentQueueMessages = [.. publisherService.GetSentQueueMessages()];
            return View("Index", model);
        }

        try
        {
            var sent = await publisherService.SendToQueueAsync(model.QueueMessageBody, model.QueueSubject);
            logger.LogInformation("Message sent to queue: {MessageId}", sent.MessageId);
            TempData["QueueSuccess"] = $"Message sent to demo-q! ID: {sent.MessageId}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send message to queue");
            TempData["QueueError"] = $"Failed to send message: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
