using System.Collections.Generic;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Dispatcher;
using Autofac;
using Autofac.Integration.WebApi;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Middleware;
using Microsoft.Owin;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.Security.OAuth;
using Microsoft.Owin.StaticFiles;
using Owin;

namespace KitsuneCommand.Web
{
    /// <summary>
    /// Configures the OWIN HTTP pipeline: middleware, auth, static files, and Web API.
    /// </summary>
    public class OwinStartup
    {
        private readonly AppSettings _settings;
        private readonly IContainer _container;

        public OwinStartup(AppSettings settings, IContainer container)
        {
            _settings = settings;
            _container = container;
        }

        public void Configuration(IAppBuilder app)
        {
            // 1. Global error handling
            app.Use<ErrorHandlingMiddleware>();

            // 2. CORS (for development with Vite)
            app.Use<CorsMiddleware>(_settings);

            // 3. OAuth2 authorization server (token endpoint)
            // Use HMAC-based data protection (DPAPI is not available on Mono/Unity)
            app.Properties["security.DataProtectionProvider"] = (Func<string[], Tuple<Func<byte[], byte[]>, Func<byte[], byte[]>>>)
                (purposes =>
                {
                    var protector = new HmacDataProtectionProvider("KitsuneCommand").Create(purposes);
                    return Tuple.Create<Func<byte[], byte[]>, Func<byte[], byte[]>>(
                        data => protector.Protect(data),
                        data => protector.Unprotect(data));
                });

            var authService = _container.Resolve<AuthService>();
            authService.EnsureAdminExists();

            var oauthOptions = new OAuthAuthorizationServerOptions
            {
                TokenEndpointPath = new PathString("/token"),
                Provider = new OAuthProvider(authService),
                AccessTokenExpireTimeSpan = TimeSpan.FromMinutes(_settings.AccessTokenExpireMinutes),
                AllowInsecureHttp = true, // Running behind game server, not directly exposed
            };

            app.UseOAuthAuthorizationServer(oauthOptions);
            app.UseOAuthBearerAuthentication(new OAuthBearerAuthenticationOptions());

            // 4. Game-ready gate (blocks /api/ until game is loaded)
            app.Use<GameReadyMiddleware>();

            // 5. Static files (Vue frontend from wwwroot/)
            var webRootPath = Path.Combine(ModEntry.ModPath, "wwwroot");
            if (Directory.Exists(webRootPath))
            {
                var fileSystem = new PhysicalFileSystem(webRootPath);

                app.UseDefaultFiles(new DefaultFilesOptions
                {
                    DefaultFileNames = new List<string> { "index.html" },
                    FileSystem = fileSystem
                });

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileSystem = fileSystem,
                    ServeUnknownFileTypes = false
                });
            }

            // 6. Web API with Autofac DI
            var config = new HttpConfiguration();

            // Use a safe assembly resolver that only returns KitsuneCommand assemblies.
            // The default resolver scans ALL loaded assemblies, which crashes when it
            // encounters HarmonyLib (references Mono.Cecil which isn't present).
            config.Services.Replace(typeof(System.Web.Http.Dispatcher.IAssembliesResolver),
                new SafeAssembliesResolver());

            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // JSON serialization settings — camelCase so the Vue frontend can use response.data.data
            config.Formatters.JsonFormatter.SerializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatString = "yyyy-MM-ddTHH:mm:ss"
            };

            // Remove XML formatter - JSON only
            config.Formatters.Remove(config.Formatters.XmlFormatter);

            config.DependencyResolver = new AutofacWebApiDependencyResolver(_container);

            config.EnsureInitialized();

            app.UseAutofacMiddleware(_container);
            app.UseAutofacWebApi(config);
            app.UseWebApi(config);
        }
    }

    /// <summary>
    /// Custom assembly resolver that only returns KitsuneCommand's own assembly.
    /// The default resolver scans ALL loaded assemblies, which crashes when it
    /// encounters HarmonyLib (it references Mono.Cecil which 7D2D doesn't ship).
    /// </summary>
    internal class SafeAssembliesResolver : IAssembliesResolver
    {
        public ICollection<Assembly> GetAssemblies()
        {
            return new[] { typeof(SafeAssembliesResolver).Assembly };
        }
    }
}
