namespace KitsuneCommand.Features
{
    /// <summary>
    /// Interface for feature modules. Each feature represents a self-contained
    /// server functionality (e.g., points system, game store, teleportation).
    /// </summary>
    public interface IFeature
    {
        string Name { get; }
        bool IsRunning { get; }
        void LoadSettings();
        void Start();
        void Stop();
    }
}
