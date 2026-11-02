using System;
using System.Net;
using System.Text;
using System.Threading;
using KitsuneCommand.Data.Entities;
using Newtonsoft.Json;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Sends ticket notifications and suspicion-log alerts to a Discord channel via webhook.
    /// All calls are fire-and-forget on the thread pool.
    /// </summary>
    public class DiscordWebhookService
    {
        public void SendTicketCreated(string webhookUrl, Ticket ticket, string message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"New Ticket #{ticket.Id}",
                        description = TruncateMessage(message),
                        color = 0x22c55e, // green
                        fields = new[]
                        {
                            new { name = "Player", value = ticket.PlayerName ?? ticket.PlayerId, inline = true },
                            new { name = "Subject", value = Truncate(ticket.Subject, 100), inline = true },
                            new { name = "Priority", value = PriorityLabel(ticket.Priority), inline = true }
                        },
                        footer = new { text = "KitsuneCommand Tickets" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            SendAsync(webhookUrl, payload);
        }

        public void SendTicketReply(string webhookUrl, Ticket ticket, TicketMessage msg)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            var isAdmin = msg.SenderType == "admin";
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"Reply on Ticket #{ticket.Id}",
                        description = TruncateMessage(msg.Message),
                        color = isAdmin ? 0x3b82f6 : 0x8b5cf6, // blue for admin, purple for player
                        fields = new[]
                        {
                            new { name = "From", value = msg.SenderName ?? msg.SenderId ?? "Unknown", inline = true },
                            new { name = "Type", value = isAdmin ? "Admin" : "Player", inline = true },
                            new { name = "Subject", value = Truncate(ticket.Subject, 100), inline = true }
                        },
                        footer = new { text = "KitsuneCommand Tickets" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            SendAsync(webhookUrl, payload);
        }

        public void SendTicketClosed(string webhookUrl, Ticket ticket, string closedBy)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"Ticket #{ticket.Id} Closed",
                        description = Truncate(ticket.Subject, 200),
                        color = 0x6b7280, // gray
                        fields = new[]
                        {
                            new { name = "Closed By", value = closedBy, inline = true },
                            new { name = "Player", value = ticket.PlayerName ?? ticket.PlayerId, inline = true }
                        },
                        footer = new { text = "KitsuneCommand Tickets" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            SendAsync(webhookUrl, payload);
        }

        public void SendSuspicionAlert(string webhookUrl, SuspicionEvent evt)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) || evt == null) return;

            var where = evt.PosX.HasValue && evt.PosZ.HasValue
                ? $"{evt.PosX.Value:F0}, {(evt.PosY ?? 0):F0}, {evt.PosZ.Value:F0}"
                : "—";
            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = $"Suspicion: {evt.Detector} / {evt.Rule}",
                        description = TruncateMessage(evt.Summary),
                        color = SeverityColor(evt.Severity),
                        fields = new[]
                        {
                            new { name = "Player", value = evt.PlayerName ?? evt.PlayerId ?? "Unknown", inline = true },
                            new { name = "Severity", value = evt.Severity ?? "low", inline = true },
                            new { name = "Position", value = where, inline = true }
                        },
                        footer = new { text = "KitsuneCommand Suspicion Log · log-only, review before acting" },
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            SendAsync(webhookUrl, payload);
        }

        private static int SeverityColor(string severity)
        {
            switch (severity)
            {
                case "high": return 0xef4444; // red
                case "medium": return 0xf59e0b; // amber
                default: return 0x6b7280; // gray
            }
        }

        private static void SendAsync(string webhookUrl, object payload)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using (var client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.ContentType] = "application/json";
                        var json = JsonConvert.SerializeObject(payload);
                        client.UploadString(webhookUrl, json);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] Discord webhook failed: {ex.Message}");
                }
            });
        }

        private static string PriorityLabel(int priority)
        {
            switch (priority)
            {
                case 0: return "Low";
                case 2: return "High";
                default: return "Normal";
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private static string TruncateMessage(string text)
        {
            return Truncate(text, 500);
        }
    }
}
