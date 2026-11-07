namespace KitsuneCommand.Features
{
    public class SafeZoneSettings
    {
        /// <summary>Master switch. Off keeps the zones but stops removing anything.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>How often the zones are swept, in seconds.</summary>
        public int SweepIntervalSeconds { get; set; } = 2;

        public List<SafeZone> Zones { get; set; } = new List<SafeZone>();
    }

    /// <summary>
    /// A rectangle on the map, in world block coordinates (X east, Z north),
    /// that zombies are removed from. It spans every height.
    /// </summary>
    public class SafeZone
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;
        public int MinX { get; set; }
        public int MinZ { get; set; }
        public int MaxX { get; set; }
        public int MaxZ { get; set; }

        /// <summary>Also remove passive animals (deer, rabbits, chickens).</summary>
        public bool RemoveAnimals { get; set; }

        /// <summary>Let blood moon horde zombies in instead of removing them.</summary>
        public bool AllowBloodMoon { get; set; }

        /// <summary>Inclusive on both edges, so a 1x1 zone holds its one block.</summary>
        public bool Contains(float x, float z) =>
            x >= MinX && x < MaxX + 1 && z >= MinZ && z < MaxZ + 1;
    }
}
