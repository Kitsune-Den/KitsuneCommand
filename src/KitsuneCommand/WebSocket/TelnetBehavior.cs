using KitsuneCommand.Core;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace KitsuneCommand.WebSocket
{
    /// <summary>
    /// WebSocket behavior for console telnet. Clients can send commands
    /// and receive real-time log output.
    /// </summary>
    public class TelnetBehavior : WebSocketBehavior
    {
        protected override void OnOpen()
        {
            // Validate token from query string
            var token = Context.QueryString["token"];
            if (string.IsNullOrEmpty(token))
            {
                Context.WebSocket.Close(CloseStatusCode.PolicyViolation, "Authentication required");
                return;
            }

            // TODO: Validate OAuth token
            // For now, accept any non-empty token (will be properly validated in Phase 2)

            // Send welcome message
            var welcome = new
            {
                eventType = "Welcome",
                data = new
                {
                    message = "Connected to KitsuneCommand WebSocket",
                    version = "2.0.0"
                }
            };

            Send(JsonConvert.SerializeObject(welcome));
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            var command = e.Data.Trim();

            if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                Context.WebSocket.Close(CloseStatusCode.Normal, "Goodbye");
                return;
            }

            // Execute command on main thread
            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    var output = SdtdConsole.Instance.ExecuteSync(command, null);
                    var result = output != null ? string.Join("\n", output) : "";

                    var reply = new WebSocketMessage<object>
                    {
                        EventType = "CommandResult",
                        Data = new { command, output = result }
                    };

                    Send(JsonConvert.SerializeObject(reply));
                }
                catch (Exception ex)
                {
                    var error = new WebSocketMessage<object>
                    {
                        EventType = "CommandResult",
                        Data = new { command, output = $"Error: {ex.Message}" }
                    };

                    Send(JsonConvert.SerializeObject(error));
                }
            }, null);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            // Clean up if needed
        }
    }
}
