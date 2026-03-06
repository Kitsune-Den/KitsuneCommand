using KitsuneCommand.Core;
using KitsuneCommand.Web.Auth;
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
        private string _username;
        private string _role;

        protected override void OnOpen()
        {
            // Validate token from query string
            var token = Context.QueryString["token"];
            if (string.IsNullOrEmpty(token))
            {
                Context.WebSocket.Close(CloseStatusCode.PolicyViolation, "Authentication required");
                return;
            }

            if (!TokenValidator.ValidateToken(token, out _username, out _role))
            {
                Context.WebSocket.Close(CloseStatusCode.PolicyViolation, "Invalid or expired token");
                return;
            }

            global::Log.Out($"[KitsuneCommand] WebSocket connected: {_username} ({_role})");

            // Send welcome message
            var welcome = new
            {
                eventType = "Welcome",
                data = new
                {
                    message = "Connected to KitsuneCommand WebSocket",
                    version = "2.0.0",
                    username = _username,
                    role = _role
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

            // Block viewers from executing commands
            if (string.Equals(_role, "viewer", StringComparison.OrdinalIgnoreCase))
            {
                var denied = new WebSocketMessage<object>
                {
                    EventType = "CommandResult",
                    Data = new { command, output = "Error: Insufficient permissions. Viewers cannot execute commands." }
                };
                Send(JsonConvert.SerializeObject(denied, EventBroadcaster.CamelCase));
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
                        Data = new { command, output = result, executedBy = _username }
                    };

                    Send(JsonConvert.SerializeObject(reply, EventBroadcaster.CamelCase));
                }
                catch (Exception ex)
                {
                    var error = new WebSocketMessage<object>
                    {
                        EventType = "CommandResult",
                        Data = new { command, output = $"Error: {ex.Message}" }
                    };

                    Send(JsonConvert.SerializeObject(error, EventBroadcaster.CamelCase));
                }
            }, null);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (!string.IsNullOrEmpty(_username))
            {
                global::Log.Out($"[KitsuneCommand] WebSocket disconnected: {_username}");
            }
        }
    }
}
