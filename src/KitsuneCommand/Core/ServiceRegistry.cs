using Autofac;
using Autofac.Integration.WebApi;
using KitsuneCommand.Configuration;
using KitsuneCommand.Data;
using KitsuneCommand.Features;
using KitsuneCommand.Services;
using KitsuneCommand.Web.Auth;

namespace KitsuneCommand.Core
{
    /// <summary>
    /// Builds the Autofac dependency injection container with all services.
    /// </summary>
    public static class ServiceRegistry
    {
        public static IContainer Build(AppSettings settings)
        {
            var builder = new ContainerBuilder();

            // Settings
            builder.RegisterInstance(settings).AsSelf().SingleInstance();

            // Core services
            builder.RegisterType<ModEventBus>().As<IModEventBus>().AsSelf().SingleInstance();
            builder.RegisterType<ConfigManager>().AsSelf().SingleInstance();
            builder.RegisterType<AuthService>().AsSelf().SingleInstance();
            builder.RegisterType<LivePlayerManager>().AsSelf().SingleInstance();
            builder.RegisterType<MapTileRenderer>().AsSelf().SingleInstance();
            builder.RegisterType<ChatPersistenceService>().AsSelf().SingleInstance();
            builder.RegisterType<GameItemCatalog>().AsSelf().SingleInstance();
            builder.RegisterType<ItemIconService>().AsSelf().SingleInstance();

            // Server management services
            builder.RegisterType<ServerConfigService>().AsSelf().SingleInstance();
            builder.RegisterType<ModManagerService>().AsSelf().SingleInstance();
            builder.RegisterType<BackupService>().AsSelf().SingleInstance();
            builder.RegisterType<DiscordWebhookService>().AsSelf().SingleInstance();
            builder.RegisterType<DiscordBotService>().AsSelf().SingleInstance();

            // Database connection factory
            builder.Register(c => new DbConnectionFactory(settings.DatabasePath))
                   .AsSelf()
                   .SingleInstance();

            // Register all repositories by convention (types ending in "Repository")
            var mainAssembly = typeof(ServiceRegistry).Assembly;
            builder.RegisterAssemblyTypes(mainAssembly)
                   .Where(t => t.Name.EndsWith("Repository"))
                   .AsImplementedInterfaces()
                   .InstancePerLifetimeScope();

            // Register all Web API controllers
            builder.RegisterApiControllers(mainAssembly);

            // Chat command service (used by ChatCommandFeature)
            builder.RegisterType<ChatCommandService>().AsSelf().SingleInstance();

            // Register feature modules
            builder.RegisterAssemblyTypes(mainAssembly)
                   .Where(t => typeof(IFeature).IsAssignableFrom(t) && !t.IsAbstract)
                   .AsImplementedInterfaces()
                   .AsSelf()
                   .SingleInstance();

            // Feature manager
            builder.RegisterType<FeatureManager>().AsSelf().SingleInstance();

            return builder.Build();
        }
    }
}
