using Microsoft.Owin;
using System.Net;

namespace KitsuneCommand.Web.Middleware
{
    /// <summary>
    /// Global error handling middleware. Catches unhandled exceptions and returns
    /// a consistent JSON error response.
    /// </summary>
    public class ErrorHandlingMiddleware : OwinMiddleware
    {
        public ErrorHandlingMiddleware(OwinMiddleware next) : base(next) { }

        public override async Task Invoke(IOwinContext context)
        {
            try
            {
                await Next.Invoke(context);
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Unhandled error: {ex.Message}");
                Log.Exception(ex);

                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";

                var errorResponse = JsonConvert.SerializeObject(new
                {
                    code = 500,
                    message = "Internal server error",
                    detail = ex.Message
                });

                await context.Response.WriteAsync(errorResponse);
            }
        }
    }
}
