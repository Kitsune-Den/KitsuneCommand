namespace KitsuneCommand.Data.Entities
{
    public class PlayerMetadata
    {
        public string PlayerId { get; set; }
        public string NameColor { get; set; }
        public string CustomTag { get; set; }
        public string Notes { get; set; }

        /// <summary>
        /// VIP tier name (matches an entry in VipPerksSettings.Tiers).
        /// null / empty = no tier ("pleb"). Set via SetTier, never via Upsert
        /// — see PlayerMetadataRepository for why the two paths are kept apart.
        /// </summary>
        public string VipTier { get; set; }
        public string UpdatedAt { get; set; }
    }
}
