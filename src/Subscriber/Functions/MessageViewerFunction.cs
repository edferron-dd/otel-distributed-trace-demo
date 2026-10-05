using System.Net;
using System.Text;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Subscriber.Models;
using Subscriber.Services;

namespace Subscriber.Functions;

/// <summary>
/// HTTP-triggered viewer that renders the messages received by the Service Bus
/// triggers. Replaces the old MVC page now that the Subscriber runs headless as
/// an Azure Function. Browse to the function root ("/" via route "{*any}") or /api/messages.
/// </summary>
public class MessageViewerFunction(MessageStore topicStore, QueueMessageStore queueStore)
{
    [Function(nameof(ViewMessages))]
    public async Task<HttpResponseData> ViewMessages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "messages")]
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        await response.WriteStringAsync(RenderHtml(topicStore.GetMessages(), queueStore.GetMessages()));
        return response;
    }

    private static string RenderHtml(
        IReadOnlyList<ReceivedMessage> topic, IReadOnlyList<ReceivedMessage> queue)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html><head><meta charset="utf-8">
            <meta http-equiv="refresh" content="10">
            <title>Received Messages</title>
            <style>
              body{font-family:system-ui,sans-serif;margin:2rem;color:#222}
              h2{margin-top:2rem}
              .badge{background:#198754;color:#fff;border-radius:1rem;padding:.1rem .6rem;font-size:.8rem}
              .badge.q{background:#fd7e14}
              table{border-collapse:collapse;width:100%;margin-top:.5rem}
              th,td{border:1px solid #ddd;padding:.4rem .6rem;text-align:left;font-size:.85rem}
              th{background:#f5f5f5}
              .muted{color:#888;font-size:.8rem}
              .empty{color:#888;padding:1rem 0}
            </style></head><body>
            <h1>Subscriber &mdash; Received Messages</h1>
            <p class="muted">Auto-refreshes every 10s. Served by an Azure Function HTTP trigger.</p>
            """);

        AppendSection(sb, "demo-topic", "badge", topic);
        AppendSection(sb, "demo-q", "badge q", queue);

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendSection(
        StringBuilder sb, string name, string badgeClass, IReadOnlyList<ReceivedMessage> messages)
    {
        sb.Append($"<h2>{Enc(name)} <span class=\"{badgeClass}\">{messages.Count}</span></h2>");

        if (messages.Count == 0)
        {
            sb.Append($"<div class=\"empty\">Waiting for messages on <strong>{Enc(name)}</strong>&hellip;</div>");
            return;
        }

        sb.Append("<table><thead><tr><th>Received (UTC)</th><th>Subject</th><th>Body</th><th>Message ID</th><th>Seq #</th></tr></thead><tbody>");
        foreach (var m in messages)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{m.ReceivedAt:yyyy-MM-dd HH:mm:ss}<br><span class=\"muted\">Enqueued: {Enc(m.EnqueuedTimeUtc)}</span></td>");
            sb.Append($"<td>{(string.IsNullOrEmpty(m.Subject) ? "&mdash;" : Enc(m.Subject))}</td>");
            sb.Append($"<td>{Enc(m.Body)}</td>");
            sb.Append($"<td class=\"muted\">{Enc(m.MessageId)}</td>");
            sb.Append($"<td>{m.SequenceNumber}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static string Enc(string? value) => HttpUtility.HtmlEncode(value ?? string.Empty);
}
