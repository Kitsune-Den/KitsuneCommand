namespace KitsuneCommand.Abstractions
{
    /// <summary>
    /// Interface for KitsuneCommand plugins. Implement this in your plugin DLL
    /// and place it in the Plugins/ directory.
    /// </summary>
    public interface IPlugin
    {
        string Name { get; }
        string Version { get; }
        string Author { get; }

        /// <summary>
        /// Called when the plugin is loaded. Use the context to register services,
        /// subscribe to events, and access the mod's APIs.
        /// </summary>
        void Initialize(PluginContext context);

        /// <summary>
        /// Called when the server is shutting down. Clean up resources here.
        /// </summary>
        void Shutdown();
    }
}
