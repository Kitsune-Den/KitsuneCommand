using System.Collections.Generic;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Defines all known serverconfig.xml fields with metadata for the config editor UI.
    /// </summary>
    public static class ServerConfigFieldDefinitions
    {
        public static List<ConfigFieldGroup> GetGroups()
        {
            return new List<ConfigFieldGroup>
            {
                new ConfigFieldGroup
                {
                    Key = "core",
                    Fields = new List<ConfigFieldDef>
                    {
                        TextField("ServerName", "My 7D2D Server", "Display name shown in the server browser"),
                        PasswordField("ServerPassword", "", "Password required to join the server (blank = no password)"),
                        TextField("ServerDescription", "A 7 Days to Die Server", "Description shown in the server browser listing"),
                        TextField("ServerLoginConfirmationText", "", "Message players must accept before joining"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "world",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("GameWorld", "Navezgane", new[] { "Navezgane", "RWG" }, description: "Navezgane = fixed map, RWG = random generated"),
                        TextField("WorldGenSeed", "SomeSeed", "Seed string used for random world generation"),
                        SelectField("WorldGenSize", "6144", new[] { "2048", "3072", "4096", "5120", "6144", "7168", "8192", "10240" }, description: "RWG world size in blocks (only applies to RWG maps)"),
                        TextField("GameName", "My Game", "Save game name — changing this starts a new save"),
                        SelectField("GameMode", "GameModeSurvival", new[] { "GameModeSurvival" }, description: "Game mode for the server"),
                        NumberField("BedrollDeadZoneSize", "15", 0, 100, "Radius in blocks where others can't place bedrolls near yours"),
                        NumberField("MaxUncoveredMapChunksPerPlayer", "131072", 0, 1000000, "Max map chunks revealed per player (131072 = full map)"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "blockDamage",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("BlockDamagePlayer", "100", DamagePercentOptions(), description: "Player block damage multiplier (%)"),
                        SelectField("BlockDamageAI", "100", DamagePercentOptions(), description: "Zombie block damage multiplier (%)"),
                        SelectField("BlockDamageAIBM", "100", DamagePercentOptions(), description: "Blood moon zombie block damage multiplier (%)"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "gameplay",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("GameDifficulty", "2", new[] { "0", "1", "2", "3", "4", "5" }, new[] { "0 - Scavenger", "1 - Adventurer", "2 - Nomad", "3 - Warrior", "4 - Survivalist", "5 - Insane" }, "Overall difficulty — affects zombie HP, damage, loot, and XP"),
                        NumberField("DayNightLength", "60", 10, 240, "Real-time minutes per in-game 24h cycle"),
                        SelectField("PlayerKillingMode", "3", new[] { "0", "1", "2", "3" }, new[] { "0 - Kill Friendly Fire Only", "1 - Kill Strangers Only", "2 - Kill Allies Only", "3 - Kill Everyone" }, "PvP rules for player-vs-player combat"),
                        NumberField("QuestProgressionDailyLimit", "3", 0, 100, "Max quest-tier progressions per day"),
                        SelectField("JarRefund", "0", new[] { "0", "25", "50", "75", "100" }, description: "Percentage of jars returned when crafting food (0 = no return)"),
                        BoolField("BiomeProgression", "true", "Zombies get harder in further biomes"),
                        NumberField("StormFreq", "100", 0, 500, "Weather storm frequency (%)"),
                        NumberField("BloodMoonFrequency", "7", 1, 100, "Blood moon horde every N days"),
                        NumberField("BloodMoonRange", "0", 0, 7, "Random +/- days added to blood moon schedule (0 = exact)"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "zombies",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("ZombieMove", "0", ZombieMoveOptions(), ZombieMoveLabelOptions(), "Daytime zombie movement speed"),
                        SelectField("ZombieMoveNight", "3", ZombieMoveOptions(), ZombieMoveLabelOptions(), "Nighttime zombie movement speed"),
                        SelectField("ZombieFeralMove", "3", ZombieMoveOptions(), ZombieMoveLabelOptions(), "Feral zombie movement speed"),
                        SelectField("ZombieBMMove", "3", ZombieMoveOptions(), ZombieMoveLabelOptions(), "Blood moon zombie movement speed"),
                        SelectField("EnemyDifficulty", "0", new[] { "0", "1" }, new[] { "0 - Normal", "1 - Feral" }, "Feral adds more challenging zombie types"),
                        NumberField("EnemySpawnMode", "1", 0, 2, "Spawn mode (0 = disabled, 1 = default, 2 = all)"),
                        NumberField("MaxSpawnedZombies", "64", 1, 500, "Max alive zombies at once — higher = more CPU usage"),
                        NumberField("MaxSpawnedAnimals", "50", 1, 500, "Max alive animals at once"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "lootAndDrops",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("LootAbundance", "100", DamagePercentOptions(), description: "Loot quantity multiplier (%)"),
                        NumberField("LootRespawnDays", "7", 1, 100, "Days before looted containers respawn"),
                        NumberField("AirDropFrequency", "72", 0, 999, "Air drop interval in in-game hours (0 = disabled)"),
                        SelectField("DropOnDeath", "1", new[] { "0", "1", "2", "3", "4" }, new[] { "0 - Nothing", "1 - Everything", "2 - Toolbelt Only", "3 - Backpack Only", "4 - Delete All" }, "What players drop when killed"),
                        SelectField("DropOnQuit", "1", new[] { "0", "1", "2", "3" }, new[] { "0 - Nothing", "1 - Everything", "2 - Toolbelt Only", "3 - Backpack Only" }, "What players drop when disconnecting"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "landClaims",
                    Fields = new List<ConfigFieldDef>
                    {
                        NumberField("LandClaimSize", "41", 1, 200, "Protected area size in blocks (41 = 20 blocks each direction)"),
                        NumberField("LandClaimDeadZone", "30", 0, 200, "Minimum distance between land claims in blocks"),
                        NumberField("LandClaimExpiryTime", "7", 1, 365, "Days before an unvisited land claim expires"),
                        SelectField("LandClaimDecayMode", "0", new[] { "0", "1", "2" }, new[] { "0 - None", "1 - Linear", "2 - Exponential" }, "How land claim protection decays over time"),
                        NumberField("LandClaimOnlineDurabilityModifier", "4", 0, 100, "Block durability multiplier when owner is online"),
                        NumberField("LandClaimOfflineDurabilityModifier", "4", 0, 100, "Block durability multiplier when owner is offline"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "networkAndSlots",
                    Fields = new List<ConfigFieldDef>
                    {
                        NumberField("ServerPort", "26900", 1024, 65535, "Main game port (also uses +1 and +2 for auxiliary connections)"),
                        SelectField("ServerVisibility", "2", new[] { "0", "1", "2" }, new[] { "0 - Not Listed", "1 - Friends Only", "2 - Public" }, "Who can see this server in the server browser"),
                        NumberField("ServerMaxPlayerCount", "8", 1, 64, "Maximum concurrent players"),
                        NumberField("ServerReservedSlots", "0", 0, 10, "Extra slots above max count for admins/mods"),
                        NumberField("ServerMaxWorldTransferSpeedKiBs", "512", 64, 10240, "Max world file transfer speed per client in KiB/s"),
                        TextField("ServerDisabledNetworkProtocols", "", "Comma-separated list of protocols to disable (e.g. SteamNetworking)"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "admin",
                    Fields = new List<ConfigFieldDef>
                    {
                        BoolField("TelnetEnabled", "true", "Enable the telnet remote console"),
                        NumberField("TelnetPort", "8081", 1024, 65535, "Port for telnet connections"),
                        PasswordField("TelnetPassword", "", "Password for telnet access"),
                        NumberField("TelnetFailedLoginLimit", "10", 0, 100, "Failed login attempts before temporary ban"),
                        NumberField("TelnetFailedLoginsBlocktime", "10", 0, 3600, "Seconds to block after too many failed telnet logins"),
                        BoolField("EACEnabled", "true", "Easy Anti-Cheat — disabling allows modded clients"),
                        NumberField("ServerAdminSlots", "0", 0, 10, "Extra slots above max player count for admins"),
                        BoolField("HideCommandExecutionLog", "0", "Hide admin command execution from non-admin players"),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "advanced",
                    Fields = new List<ConfigFieldDef>
                    {
                        TextField("ServerWebsiteURL", "", "URL shown in the server browser as the server's website"),
                        BoolField("TerminalWindowEnabled", "true", "Show the terminal/console window on the server"),
                        BoolField("WebDashboardEnabled", "false", "Enable the built-in 7D2D web dashboard"),
                        NumberField("WebDashboardPort", "8080", 1024, 65535, "Port for the built-in web dashboard"),
                        TextField("WebDashboardUrl", "", "External dashboard URL for reverse proxy setups"),
                    }
                },
            };
        }

        private static ConfigFieldDef TextField(string key, string defaultValue, string description = null)
            => new ConfigFieldDef { Key = key, Type = "text", DefaultValue = defaultValue, Description = description };

        private static ConfigFieldDef PasswordField(string key, string defaultValue, string description = null)
            => new ConfigFieldDef { Key = key, Type = "password", DefaultValue = defaultValue, Description = description };

        private static ConfigFieldDef NumberField(string key, string defaultValue, int min, int max, string description = null)
            => new ConfigFieldDef { Key = key, Type = "number", DefaultValue = defaultValue, Min = min, Max = max, Description = description };

        private static ConfigFieldDef BoolField(string key, string defaultValue, string description = null)
            => new ConfigFieldDef { Key = key, Type = "bool", DefaultValue = defaultValue, Description = description };

        private static ConfigFieldDef SelectField(string key, string defaultValue, string[] options, string[] labels = null, string description = null)
            => new ConfigFieldDef { Key = key, Type = "select", DefaultValue = defaultValue, Options = options, Labels = labels, Description = description };

        private static string[] DamagePercentOptions()
            => new[] { "25", "50", "75", "100", "125", "150", "175", "200", "300" };

        private static string[] ZombieMoveOptions()
            => new[] { "0", "1", "2", "3", "4" };

        private static string[] ZombieMoveLabelOptions()
            => new[] { "0 - Strolling", "1 - Slow", "2 - Jog", "3 - Run", "4 - Sprint" };
    }

    public class ConfigFieldGroup
    {
        public string Key { get; set; }
        public List<ConfigFieldDef> Fields { get; set; } = new List<ConfigFieldDef>();
    }

    public class ConfigFieldDef
    {
        public string Key { get; set; }
        public string Type { get; set; } // text, password, number, bool, select
        public string DefaultValue { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }
        public string[] Options { get; set; }
        public string[] Labels { get; set; }
        public string Description { get; set; }
    }
}
