namespace KitsuneCommand.Features
{
    /// <summary>
    /// Discovers, initializes, and manages all feature modules.
    /// </summary>
    public class FeatureManager
    {
        private readonly IEnumerable<IFeature> _features;

        public FeatureManager(IEnumerable<IFeature> features)
        {
            _features = features;
        }

        public void InitializeAll()
        {
            foreach (var feature in _features)
            {
                try
                {
                    feature.LoadSettings();
                    feature.Start();
                }
                catch (Exception ex)
                {
                    Log.Error($"[KitsuneCommand] Failed to initialize feature '{feature.Name}': {ex.Message}");
                }
            }

            Log.Out($"[KitsuneCommand] {_features.Count()} feature(s) initialized.");
        }

        public void ShutdownAll()
        {
            foreach (var feature in _features)
            {
                feature.Stop();
            }
        }

        public IFeature GetFeature(string name)
        {
            return _features.FirstOrDefault(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<IFeature> GetAllFeatures() => _features;
    }
}
