using Microsoft.Owin;
using KitsuneCommand.Configuration;

namespace KitsuneCommand.Web.Middleware
{
    /// <summary>
    /// CORS middleware for development. Allows the Vite dev server to make requests
    /// to the OWIN API server.
    /// </summary>
    public class CorsMiddleware : OwinMiddleware
    {
        private readonly AppSettings _settings;

        public CorsMiddleware(OwinMiddleware next, AppSettings settings) : base(next)
        {
            _settings = settings;
        }

        public override async Task Invoke(IOwinContext context)
        {
            if (_settings.EnableCors)
            {
                var origins = _settings.CorsOrigins ?? "http://localhost:5173";
                context.Response.Headers.Set("Access-Control-Allow-Origin", origins);
                context.Response.Headers.Set("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                context.Response.Headers.Set("Access-Control-Allow-Headers", "Content-Type, Authorization");
                context.Response.Headers.Set("Access-Control-Allow-Credentials", "true");

                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
                    return;
                }
            }

            await Next.Invoke(context);
        }
    }
}
