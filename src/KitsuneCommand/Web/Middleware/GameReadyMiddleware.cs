using Microsoft.Owin;
using KitsuneCommand.Core;

namespace KitsuneCommand.Web.Middleware
{
    /// <summary>
    /// Rejects API requests if the game world hasn't finished loading yet.
    /// Static files (the web panel) are still served.
    /// </summary>
    public class GameReadyMiddleware : OwinMiddleware
    {
        public GameReadyMiddleware(OwinMiddleware next) : base(next) { }

        public override async Task Invoke(IOwinContext context)
        {
            var path = context.Request.Path.Value;

            // Allow static files and the token endpoint before game is ready
            if (path.StartsWith("/api/") && !ModEntry.IsGameStartDone)
            {
                context.Response.StatusCode = 503;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonConvert.SerializeObject(new
                {
                    code = 503,
                    message = "Server is still starting. Please wait for the game world to load."
                }));
                return;
            }

            await Next.Invoke(context);
        }
    }
}
