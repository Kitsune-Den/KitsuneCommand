using System;
using System.Net.Http;
using System.Web.Http.Hosting;
using Microsoft.Owin;

namespace KitsuneCommand.Web
{
    /// <summary>
    /// Tells WebAPI not to fully buffer the request body for endpoints that stream
    /// large uploads directly to disk.
    ///
    /// Without this, the default <see cref="IHostBufferPolicySelector"/> buffers
    /// the entire HTTP request into memory before invoking the action method.
    /// That defeats <c>MultipartFileStreamProvider</c>: even though the provider
    /// is designed to stream to disk in constant memory, it ends up reading from
    /// an already-buffered MemoryStream — so the OOM hits at buffer time, before
    /// the controller code even runs. Symptom: large modpack uploads (~500MB+)
    /// crash the host process during upload, never reaching the handler.
    ///
    /// This policy is registered in <see cref="OwinStartup"/>. It opts out of
    /// buffering only for the upload route(s) listed below; everything else
    /// continues to use the WebAPI default (buffered), keeping small endpoints
    /// fast and simple.
    /// </summary>
    public class StreamingUploadBufferPolicy : IHostBufferPolicySelector
    {
        private static readonly string[] StreamingRoutes =
        {
            "/api/mods/upload"
        };

        public bool UseBufferedInputStream(object hostContext)
        {
            if (hostContext is IOwinContext owinContext &&
                owinContext.Request?.Path.Value is string path)
            {
                foreach (var route in StreamingRoutes)
                {
                    if (path.StartsWith(route, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return true;
        }

        public bool UseBufferedOutputStream(HttpResponseMessage response) => true;
    }
}
