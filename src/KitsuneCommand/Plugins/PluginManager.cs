using System.Reflection;
using Autofac;

namespace KitsuneCommand.Plugins
{
    /// <summary>
    /// Loads and manages KitsuneCommand plugins from the Plugins/ directory.
    /// </summary>
    public static class PluginManager
    {
        private static readonly List<IPlugin> _plugins = new List<IPlugin>();
        private static readonly List<Assembly> _pluginAssemblies = new List<Assembly>();

        public static IReadOnlyList<IPlugin> LoadedPlugins => _plugins.AsReadOnly();
        public static IReadOnlyList<Assembly> PluginAssemblies => _pluginAssemblies.AsReadOnly();

        public static void LoadPlugins(string pluginsDir)
        {
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
                return;
            }

            var dllFiles = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories);

            foreach (var dllFile in dllFiles)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dllFile);
                    _pluginAssemblies.Add(assembly);

                    var pluginTypes = assembly.GetExportedTypes()
                        .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract);

                    foreach (var pluginType in pluginTypes)
                    {
                        var plugin = (IPlugin)Activator.CreateInstance(pluginType);
                        _plugins.Add(plugin);
                        Log.Out($"[KitsuneCommand] Loaded plugin: {plugin.Name} v{plugin.Version} by {plugin.Author}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] Failed to load plugin from {Path.GetFileName(dllFile)}: {ex.Message}");
                }
            }
        }

        public static void RegisterServices(ContainerBuilder builder)
        {
            foreach (var assembly in _pluginAssemblies)
            {
                // Register plugin controllers
                builder.RegisterApiControllers(assembly);

                // Register plugin repositories
                builder.RegisterAssemblyTypes(assembly)
                    .Where(t => t.Name.EndsWith("Repository"))
                    .AsImplementedInterfaces()
                    .InstancePerLifetimeScope();
            }
        }

        public static void InitializePlugins(PluginContext context)
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.Initialize(context);
                    Log.Out($"[KitsuneCommand] Plugin initialized: {plugin.Name}");
                }
                catch (Exception ex)
                {
                    Log.Error($"[KitsuneCommand] Failed to initialize plugin '{plugin.Name}': {ex.Message}");
                }
            }
        }

        public static void ShutdownPlugins()
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.Shutdown();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] Error shutting down plugin '{plugin.Name}': {ex.Message}");
                }
            }
        }
    }
}
