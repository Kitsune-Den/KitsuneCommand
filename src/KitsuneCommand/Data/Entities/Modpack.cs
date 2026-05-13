namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// The server's player-facing modpack. Singleton — at most one row exists.
    /// The status field controls visibility on the login-page download CTA.
    /// </summary>
    public class Modpack
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }

        /// <summary>"draft" | "published" | "archived"</summary>
        public string Status { get; set; }

        /// <summary>On-disk filename inside the modpacks dir. Null until built.</summary>
        public string Filename { get; set; }

        public long SizeBytes { get; set; }
        public int ModCount { get; set; }

        /// <summary>JSON array of mod folder names included in the pack.</summary>
        public string ModList { get; set; }

        public string Description { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public int DownloadCount { get; set; }
    }
}
