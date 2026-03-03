using System.Net;
using System.Security.Claims;
using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Chat history and messaging endpoints.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/chat")]
    public class ChatController : ApiController
    {
        private readonly IChatRecordRepository _chatRepo;

        public ChatController(IChatRecordRepository chatRepo)
        {
            _chatRepo = chatRepo;
        }

        /// <summary>
        /// Get paginated chat history with optional search and chat type filter.
        /// </summary>
        [HttpGet]
        [Route("history")]
        public IHttpActionResult GetHistory(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string search = null,
            [FromUri] int? chatType = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _chatRepo.GetHistory(pageIndex, pageSize, search, chatType);
            var total = _chatRepo.GetTotalCount(search, chatType);

            return Ok(ApiResponse.Ok(new PaginatedResponse<Data.Entities.ChatRecord>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        /// <summary>
        /// Send a chat message from the web panel into the game.
        /// Viewers are not allowed to send messages.
        /// </summary>
        [HttpPost]
        [Route("send")]
        public IHttpActionResult SendMessage([FromBody] SendChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest("Message is required.");

            // Check role — viewers cannot send chat
            var identity = User.Identity as ClaimsIdentity;
            var role = identity?.FindFirst(ClaimTypes.Role)?.Value;
            if (string.Equals(role, "viewer", StringComparison.OrdinalIgnoreCase))
                return Content(HttpStatusCode.Forbidden,
                    ApiResponse.Error(403, "Viewers cannot send chat messages."));

            var displayName = identity?.FindFirst("display_name")?.Value ?? "Server";

            // Build the console command
            string cmd;
            if (string.IsNullOrWhiteSpace(request.TargetPlayer))
            {
                // Global message: say "[WebAdmin] message"
                cmd = $"say \"[{displayName}] {EscapeQuotes(request.Message)}\"";
            }
            else
            {
                // Private message: pm "player" "[WebAdmin] message"
                cmd = $"pm \"{EscapeQuotes(request.TargetPlayer)}\" \"[{displayName}] {EscapeQuotes(request.Message)}\"";
            }

            var output = ExecuteConsoleCommand(cmd);
            return Ok(ApiResponse.Ok(new { output }));
        }

        /// <summary>
        /// Executes a console command on the main thread and returns the output.
        /// </summary>
        private static string ExecuteConsoleCommand(string command)
        {
            string result = null;
            var waitHandle = new ManualResetEventSlim(false);

            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    var output = SdtdConsole.Instance.ExecuteSync(command, null);
                    result = output != null ? string.Join("\n", output) : "";
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                }
                finally
                {
                    waitHandle.Set();
                }
            }, null);

            waitHandle.Wait(TimeSpan.FromSeconds(10));
            return result ?? "Command timed out.";
        }

        private static string EscapeQuotes(string input)
            => input?.Replace("\"", "\\\"") ?? "";
    }
}
