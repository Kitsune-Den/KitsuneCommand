namespace KitsuneCommand.Web.Models
{
    /// <summary>
    /// The running game's Sandbox option table (7D2D 3.0+), read live from
    /// SandboxOptionManager. Everything the editor needs to decode and encode a
    /// SandboxCode for exactly this game build. See SandboxTableService.
    /// </summary>
    public class SandboxTable
    {
        /// <summary>e.g. "V 3.3.0 (b18)".</summary>
        public string GameVersion { get; set; }

        /// <summary>The code format letter (SandboxOptionManager.currentVersion), first char of every code.</summary>
        public string CodeVersion { get; set; }

        /// <summary>Category tabs in the game's order.</summary>
        public List<SandboxCategoryInfo> Categories { get; set; } = new List<SandboxCategoryInfo>();

        /// <summary>
        /// Every option, grouped by category in the game's registration order —
        /// the order the game writes a code in, so encoding in this order gives
        /// the same code as the game's own copy button.
        /// </summary>
        public List<SandboxOptionInfo> Options { get; set; } = new List<SandboxOptionInfo>();

        /// <summary>Built-in, user and modded presets (not the "Custom" placeholder).</summary>
        public List<SandboxPresetInfo> Presets { get; set; } = new List<SandboxPresetInfo>();
    }

    public class SandboxCategoryInfo
    {
        public string Name { get; set; }
        public string Label { get; set; }
    }

    public class SandboxOptionInfo
    {
        /// <summary>Position in the SandboxOptions enum — the two-letter key in a code.</summary>
        public int Index { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        /// <summary>"int", "float" or "bool".</summary>
        public string Kind { get; set; }
        /// <summary>Index into Values of the game's default — the third letter in a code.</summary>
        public int DefaultIndex { get; set; }
        public List<SandboxValueInfo> Values { get; set; } = new List<SandboxValueInfo>();
        /// <summary>Other options this one switches off at some values, or null.</summary>
        public SandboxDisablesInfo Disables { get; set; }
    }

    public class SandboxValueInfo
    {
        public string Label { get; set; }
        /// <summary>The raw value, invariant culture ("0.5", "100", "True").</summary>
        public string Value { get; set; }
    }

    public class SandboxDisablesInfo
    {
        /// <summary>Names of the options that are disabled.</summary>
        public List<string> Options { get; set; } = new List<string>();
        /// <summary>Value indices of the owning option at which they are disabled.</summary>
        public List<int> WhenValueIndices { get; set; } = new List<int>();
    }

    public class SandboxPresetInfo
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        public string GroupLabel { get; set; }
        /// <summary>The game's skull rating (1 easiest), 0 when it has none.</summary>
        public int Rating { get; set; }
        public bool IsDefault { get; set; }
        public bool IsUser { get; set; }
        public bool IsModded { get; set; }
        /// <summary>The preset's settings, option name → value index (default options omitted).</summary>
        public Dictionary<string, int> Values { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>GET /api/config/sandbox.</summary>
    public class SandboxConfigResponse
    {
        /// <summary>False on a pre-3.0 server (no Sandbox system); the rest is then null.</summary>
        public bool Supported { get; set; }
        public SandboxTable Table { get; set; }
        /// <summary>
        /// The code the running world was started with (GameStats). Differs from
        /// serverconfig.xml's SandboxCode when a change is saved but not yet restarted.
        /// </summary>
        public string LiveCode { get; set; }
        /// <summary>Why Supported is false or Table is null, when it isn't just "pre-3.0".</summary>
        public string Error { get; set; }
    }
}
