using System;

namespace KitsuneCommand.Abstractions
{
    /// <summary>
    /// Context provided to plugins during initialization. Grants access to
    /// the event bus, service container, and plugin-specific data paths.
    /// </summary>
    public class PluginContext
    {
        public IModEventBus EventBus { get; }
        public IServiceProvider Services { get; }
        public string PluginDataPath { get; }
        public string ModPath { get; }

        public PluginContext(IModEventBus eventBus, IServiceProvider services, string pluginDataPath, string modPath)
        {
            EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            Services = services ?? throw new ArgumentNullException(nameof(services));
            PluginDataPath = pluginDataPath ?? throw new ArgumentNullException(nameof(pluginDataPath));
            ModPath = modPath ?? throw new ArgumentNullException(nameof(modPath));
        }
    }
}
