using System.Collections.Generic;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Defines serverconfig.xml fields with metadata for the config editor UI.
    ///
    /// VERSION COVERAGE
    ///   • 2.x: every per-property field below is authoritative and edited individually.
    ///   • 3.0 ("Dead Hot Summer", 2026-06): 7D2D moved the world/gameplay "sandbox"
    ///     settings (difficulty, XP, blood moon, loot, zombie speeds, land claims, etc.)
    ///     out of serverconfig.xml and into the in-game Sandbox, encoded as a single
    ///     "SandboxCode" property. On a 3.0 server the individual sandbox-governed
    ///     properties are IGNORED in favor of SandboxCode. We keep those fields here so
    ///     2.x servers still edit cleanly (the "keep the current structure for 2.x"
    ///     requirement) and add SandboxCode alongside them (additive, low-risk).
    ///     A version-aware editor that HIDES the sandbox-governed fields when a 3.0 server
    ///     is detected is the planned follow-up — but the exact removed/kept split must be
    ///     confirmed against a pristine 3.0 default serverconfig.xml first, so do NOT
    ///     delete any field here until then. (The serverconfig property is "SandboxCode"
    ///     per 3.0 hosting docs; the binary also exposes a "ServerSandboxCode" accessor —
    ///     reconcile the exact key against the pristine default during the follow-up.)
    ///
    /// VERSION-AGNOSTIC BY DESIGN
    ///   • Properties not modeled here are preserved untouched on save (see
    ///     ServerConfigService.SaveConfig), so a 2.x box never loses settings KC doesn't
    ///     render — and the "Folder and file locations" group (AdminFileName /
    ///     UserDataFolder / SaveGameFolder) is intentionally omitted: operators shouldn't
    ///     repoint data paths on a live world from a web panel.
    ///   • GameWorld options are merged at runtime with worlds discovered on disk (see
    ///     ServerConfigService.GetAvailableWorlds), so 3.0 Pregen worlds appear in the
    ///     dropdown automatically without being hardcoded.
    ///
    /// Descriptions are written to be useful at editing-time — what the field does, a
    /// sensible default, and the side-effect of cranking it up or down. Tone is warm
    /// but practical; the Den voice peeks through where it helps, never at the cost of
    /// clarity.
    /// </summary>
    public static class ServerConfigFieldDefinitions
    {
        public static List<ConfigFieldGroup> GetGroups()
        {
            var groups = new List<ConfigFieldGroup>
            {
                new ConfigFieldGroup
                {
                    Key = "core",
                    Fields = new List<ConfigFieldDef>
                    {
                        TextField("ServerName", "My 7D2D Server",
                            "The name shown in the 7D2D server browser. Keep it recognizable — this is how players find you."),
                        PasswordField("ServerPassword", "",
                            "Required to join the server. Leave blank for an open server; set a password to gate it to friends-only."),
                        TextField("ServerDescription", "A 7 Days to Die Server",
                            "Shown in the server browser under your name. Supports [RRGGBB]color[-] tags — e.g. [FF6B35]Kitsune Den[-]."),
                        TextField("ServerLoginConfirmationText", "",
                            "Rules or welcome message players must accept before joining. Blank = no confirmation dialog."),
                        TextField("ServerWebsiteURL", "",
                            "Shown as a clickable link in the server browser. Usually your Discord invite or community page."),
                        SelectField("Region", "NorthAmericaEast",
                            new[] { "NorthAmericaEast", "NorthAmericaWest", "CentralAmerica", "SouthAmerica", "Europe", "Russia", "Asia", "MiddleEast", "Africa", "Oceania" },
                            description: "Region tag used by the browser's region filter. Pick the one closest to your host for best default visibility."),
                        TextField("Language", "English",
                            "Primary language players should expect. Use the English name (e.g. \"German\", not \"Deutsch\")."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "world",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("GameWorld", "Navezgane", new[] { "Navezgane", "RWG" },
                            description: "Navezgane is the hand-built map (fast boot). RWG generates a fresh random world from the seed below."),
                        TextField("WorldGenSeed", "SomeSeed",
                            "Seed string for RWG. Same seed + size always produces the same layout — share it with friends to coordinate worlds."),
                        SelectField("WorldGenSize", "6144", new[] { "6144", "8192", "10240" },
                            description: "Width of the generated RWG world in blocks. Bigger = more variety and longer generation time (5–20 min)."),
                        TextField("GameName", "My Game",
                            "Save name for this world's state. Changing this creates a brand new save — handy for a fresh start on the same seed."),
                        SelectField("GameMode", "GameModeSurvival", new[] { "GameModeSurvival" },
                            description: "Only GameModeSurvival ships with V2. Here for forward compatibility."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "player",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("PlayerKillingMode", "3",
                            new[] { "0", "1", "2", "3" },
                            new[] { "No Killing (PvE)", "Kill Allies Only", "Kill Strangers Only", "Kill Everyone (PvP)" },
                            "PvP rules. \"No Killing\" makes this a pure PvE server."),
                        SelectField("DeathPenalty", "1",
                            new[] { "0", "1", "2", "3" },
                            new[] { "Nothing", "Classic XP Penalty", "Injured (debuffs persist)", "Permanent Death" },
                            "What happens when you die. \"Nothing\" is the kindest option; Permanent Death wipes your character."),
                        SelectField("DropOnDeath", "1",
                            new[] { "0", "1", "2", "3", "4" },
                            new[] { "Nothing (keep gear)", "Everything", "Toolbelt Only", "Backpack Only", "Delete All" },
                            "What drops to a lootable backpack on death. \"Nothing\" keeps all gear with you."),
                        SelectField("DropOnQuit", "1",
                            new[] { "0", "1", "2", "3" },
                            new[] { "Nothing", "Everything", "Toolbelt Only", "Backpack Only" },
                            "What drops if a player disconnects mid-game. Usually left at Nothing."),
                        NumberField("PlayerSafeZoneLevel", "5", 0, 100,
                            "New players at or below this level spawn inside a temporary safe zone (no zombies). Onboarding buffer."),
                        NumberField("PlayerSafeZoneHours", "5", 0, 100,
                            "How many in-game hours the safe zone lasts before enemies can spawn."),
                        SelectField("AllowSpawnNearFriend", "2",
                            new[] { "0", "1", "2" },
                            new[] { "Disabled", "Always", "Forest Biome Only" },
                            "Can new players choose to spawn near a friend on join? Forest-only is a nice compromise."),
                        SelectField("CameraRestrictionMode", "0",
                            new[] { "0", "1", "2" },
                            new[] { "Free (player's choice)", "First Person Only", "Third Person Only" },
                            "Restrict what camera modes players can use. \"Free\" lets them swap mid-game."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "gameplay",
                    Fields = new List<ConfigFieldDef>
                    {
                        TextField("SandboxCode", "",
                            "7D2D 3.0+ only. Paste the Sandbox code you generate in-game (New Game → Sandbox Options → copy code). On a 3.0 server this single value drives difficulty, XP, blood moon, loot, zombie behavior, land claims, and the rest of the world ruleset — the matching individual settings in this editor are ignored in its favor. Leave blank on 2.x servers, where those individual settings apply instead."),
                        SelectField("GameDifficulty", "2",
                            new[] { "0", "1", "2", "3", "4", "5" },
                            new[] { "Scavenger", "Adventurer", "Nomad", "Warrior", "Survivalist", "Insane" },
                            "Global difficulty. Nomad is the vanilla sweet spot; Survivalist for veterans; Insane for pain."),
                        NumberField("DayNightLength", "60", 10, 240,
                            "Real-time minutes per in-game 24h day. 60 = one hour real per in-game day. Pair with DayLightLength below to set the day/night split."),
                        NumberField("DayLightLength", "18", 1, 23,
                            "In-game hours the sun is up (rest is night). 18 is a generous ~75% daylight; drop to 12 for balanced day/night."),
                        NumberField("QuestProgressionDailyLimit", "3", 0, 100,
                            "Max quests that count toward trader-tier progression each day. Extra quests still pay out rewards."),
                        SelectField("JarRefund", "0",
                            new[] { "0", "5", "10", "20", "30", "40", "50", "60", "70", "80", "90", "100" },
                            description: "Percent chance an empty jar returns after drinking. 0 removes jar management; 100 is infinite jars."),
                        BoolField("BiomeProgression", "true",
                            "Enables biome hazards + loot-stage caps so players earn their way into harder zones. Turn off for a flatter curve."),
                        NumberField("StormFreq", "100", 0, 500,
                            "How often weather storms roll in. 0 disables storms; 500 is near-constant."),
                        BoolField("BuildCreate", "false",
                            "Creative mode toggle. Grants all items, no resource cost. Leave off unless you're building a showcase."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "blockDamage",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("BlockDamagePlayer", "100", DamagePercentOptions(), DamagePercentLabels(),
                            "How hard players hit blocks. 100% is vanilla; crank up for a quicker-build pace, down for long mining sessions."),
                        SelectField("BlockDamageAI", "100", DamagePercentOptions(), DamagePercentLabels(),
                            "How hard normal zombies damage blocks during regular days."),
                        SelectField("BlockDamageAIBM", "100", DamagePercentOptions(), DamagePercentLabels(),
                            "How hard blood-moon zombies damage blocks. The big one — this is what eats your base on horde night."),
                        SelectField("XPMultiplier", "100", DamagePercentOptions(), DamagePercentLabels(),
                            "Multiplies all XP gained. 125% is a gentle boost, 200% is generous, 300% is a speedrun."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "zombies",
                    Fields = new List<ConfigFieldDef>
                    {
                        BoolField("EnemySpawnMode", "true",
                            "Master switch for zombie spawns. Off means a quiet world with only scripted POI sleepers — not recommended for survival."),
                        SelectField("EnemyDifficulty", "0",
                            new[] { "0", "1" },
                            new[] { "Normal", "Feral (tougher roster)" },
                            "Feral adds higher-tier zombie variants to the mix. Bumps perceived difficulty noticeably."),
                        SelectField("ZombieFeralSense", "0",
                            new[] { "0", "1", "2", "3" },
                            new[] { "Off", "Day only", "Night only", "Always" },
                            "When feral zombies can home in on players through walls/roofs."),
                        SelectField("ZombieMove", "0", ZombieMoveOptions(), ZombieMoveLabelOptions(),
                            "Daytime zombie speed. Walk is classic; Jog makes early game meaner."),
                        SelectField("ZombieMoveNight", "3", ZombieMoveOptions(), ZombieMoveLabelOptions(),
                            "Nighttime zombie speed. Sprint/Nightmare at night is a whole other game."),
                        SelectField("ZombieFeralMove", "3", ZombieMoveOptions(), ZombieMoveLabelOptions(),
                            "Feral variants always move at this speed regardless of day/night."),
                        SelectField("ZombieBMMove", "3", ZombieMoveOptions(), ZombieMoveLabelOptions(),
                            "Blood moon horde speed. Sprint is intense; Walk is a gentle introduction."),
                        SelectField("AISmellMode", "3",
                            new[] { "0", "1", "2", "3", "4", "5" },
                            new[] { "Off", "Walk", "Jog", "Run", "Sprint", "Nightmare" },
                            "How fast zombies track scents (food smells, rotting flesh, etc). Higher = harder to lose them."),
                        NumberField("MaxSpawnedZombies", "64", 1, 500,
                            "Server-wide cap on alive zombies. 64 is vanilla; raise for chaos, lower if you see tick lag during hordes."),
                        NumberField("MaxSpawnedAnimals", "50", 1, 500,
                            "Server-wide cap on alive wildlife. Animals are cheap — raise if players complain hunting feels dead."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "bloodMoon",
                    Fields = new List<ConfigFieldDef>
                    {
                        NumberField("BloodMoonFrequency", "7", 0, 100,
                            "Blood moon every N days. 7 is vanilla. 0 disables horde night entirely — the world still has zombies, they just don't swarm."),
                        NumberField("BloodMoonRange", "0", 0, 7,
                            "Randomize blood moon day by ±N. 0 means exactly every Nth day; set to 2 for a 5–9 day window. Keeps things unpredictable."),
                        NumberField("BloodMoonWarning", "8", -1, 22,
                            "Hour of the day the red warning appears on-screen. 8 gives a day's warning; -1 disables the visual."),
                        NumberField("BloodMoonEnemyCount", "8", 1, 64,
                            "Max zombies per player during the horde. Per-player, so a 4-player server can see up to 4×N alive at peak."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "lootAndDrops",
                    Fields = new List<ConfigFieldDef>
                    {
                        SelectField("LootAbundance", "100", DamagePercentOptions(), DamagePercentLabels(),
                            "Multiplier on container loot quantity. 100% is vanilla; 200% if you want pockets full faster."),
                        NumberField("LootRespawnDays", "7", 1, 100,
                            "In-game days before a looted container refills. 7 is vanilla; raise for a more scarcity-driven economy."),
                        NumberField("AirDropFrequency", "72", 0, 999,
                            "Hours between air drops. 72 = every 3 in-game days. Set to 0 to disable air drops entirely."),
                        BoolField("AirDropMarker", "true",
                            "Shows a marker on the map/compass when an air drop lands. Turn off for \"find it yourself\" mode."),
                        NumberField("PartySharedKillRange", "100", 0, 10000,
                            "Distance in blocks within which party members share XP and quest-kill credit. 100 is friendly; very high = server-wide."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "landClaims",
                    Fields = new List<ConfigFieldDef>
                    {
                        NumberField("LandClaimCount", "3", 1, 50,
                            "Keystones a single player can place. Each defines a protected build area."),
                        NumberField("LandClaimSize", "41", 1, 200,
                            "Edge length of the protected cube in blocks (centered on the keystone). 41 is vanilla."),
                        NumberField("LandClaimDeadZone", "30", 0, 200,
                            "Minimum blocks between different players' keystones. Prevents claim-overlap grief."),
                        NumberField("LandClaimExpiryTime", "7", 1, 365,
                            "Real-world days an offline player's claim stays active. Expires after that — their base becomes fair game."),
                        SelectField("LandClaimDecayMode", "0",
                            new[] { "0", "1", "2" },
                            new[] { "Slow (linear)", "Fast (exponential)", "None (full protection)" },
                            "How claim protection weakens while the owner is offline."),
                        NumberField("LandClaimOnlineDurabilityModifier", "4", 0, 100,
                            "Block hardness multiplier inside a claim when the owner is online. 4 = 4× tougher; 0 = indestructible."),
                        NumberField("LandClaimOfflineDurabilityModifier", "4", 0, 100,
                            "Same idea, but when the owner is offline. Raise for a more forgiving offline experience."),
                        NumberField("LandClaimOfflineDelay", "0", 0, 1440,
                            "Minutes after logout before offline protection kicks in. 0 is instant — useful if players log-out-to-escape."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "networkAndSlots",
                    Fields = new List<ConfigFieldDef>
                    {
                        NumberField("ServerPort", "26900", 1024, 65535,
                            "Main UDP game port. The server also listens on +1 and +2. Forward all three on your router."),
                        SelectField("ServerVisibility", "2",
                            new[] { "0", "1", "2" },
                            new[] { "Not Listed", "Friends Only", "Public" },
                            "How the server appears in the browser. \"Not Listed\" still accepts direct-IP connections."),
                        NumberField("ServerMaxPlayerCount", "8", 1, 64,
                            "Maximum concurrent players. 8 is a friendly community size; 16+ needs a beefier host."),
                        NumberField("ServerReservedSlots", "0", 0, 10,
                            "Slots reserved for players with high permission. 0 disables the system."),
                        NumberField("ServerReservedSlotsPermission", "100", 0, 1000,
                            "Minimum permission level (0 is highest) required to claim a reserved slot."),
                        NumberField("ServerAdminSlots", "0", 0, 10,
                            "Extra admin-only slots above MaxPlayerCount. Lets admins always join a full server."),
                        NumberField("ServerAdminSlotsPermission", "0", 0, 1000,
                            "Minimum permission level required to use the admin-only slots above."),
                        NumberField("ServerMaxWorldTransferSpeedKiBs", "512", 64, 10240,
                            "Max KiB/s used when sending the world to a freshly-joined player. Caps at ~1300 regardless."),
                        TextField("ServerDisabledNetworkProtocols", "",
                            "Comma-separated protocols to disable (LiteNetLib, SteamNetworking). Dedicated servers on public IPs often disable SteamNetworking."),
                        NumberField("ServerMaxAllowedViewDistance", "12", 6, 12,
                            "Cap on how far clients can render. Higher = more CPU and memory. 12 is the engine max."),
                        NumberField("MaxQueuedMeshLayers", "1000", 100, 10000,
                            "Buffer size for chunk mesh generation. Lower saves memory; higher avoids gen stutter with many players."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "admin",
                    Fields = new List<ConfigFieldDef>
                    {
                        BoolField("TelnetEnabled", "true",
                            "Enables the telnet interface for remote console access. Needed for most external admin tooling."),
                        NumberField("TelnetPort", "8081", 1024, 65535,
                            "Port the telnet listener binds to."),
                        PasswordField("TelnetPassword", "",
                            "Telnet access password. If blank, telnet only binds to localhost (still safe for local tools like KC)."),
                        NumberField("TelnetFailedLoginLimit", "10", 0, 100,
                            "Failed password attempts before the client is temporarily blocked."),
                        NumberField("TelnetFailedLoginsBlocktime", "10", 0, 3600,
                            "Seconds a telnet client stays blocked after hitting the failure limit."),
                        BoolField("EACEnabled", "true",
                            "Easy Anti-Cheat. Must be OFF for Harmony-based mods (KitsuneCommand included) to load on clients."),
                        BoolField("ServerAllowCrossplay", "false",
                            "Enables crossplay with console players. Comes with default player-slot restrictions."),
                        BoolField("IgnoreEOSSanctions", "false",
                            "Allow players with EOS sanctions against them. Leave off unless you have a specific reason."),
                        SelectField("HideCommandExecutionLog", "0",
                            new[] { "0", "1", "2", "3" },
                            new[] { "Show all", "Hide from telnet/control panel", "Also hide from clients", "Hide everything" },
                            "How loudly admin command execution is logged across interfaces."),
                        BoolField("PersistentPlayerProfiles", "false",
                            "Locks each player to the profile they first joined with. Good for roleplay servers; blocks profile-swap tricks."),
                    }
                },
                new ConfigFieldGroup
                {
                    Key = "advanced",
                    Fields = new List<ConfigFieldDef>
                    {
                        BoolField("TerminalWindowEnabled", "true",
                            "Shows the log window on Windows hosts. No effect on Linux."),
                        BoolField("WebDashboardEnabled", "false",
                            "TFP's built-in web dashboard. Leave OFF — KitsuneCommand replaces this with a proper panel."),
                        NumberField("WebDashboardPort", "8080", 1024, 65535,
                            "Port for TFP's built-in dashboard (if you enabled it above)."),
                        TextField("WebDashboardUrl", "",
                            "External URL if the built-in dashboard is behind a reverse proxy. Full URL with scheme."),
                        BoolField("EnableMapRendering", "false",
                            "Renders map tiles while players explore. Uses CPU. KitsuneCommand has its own map renderer that doesn't need this."),
                        NumberField("MaxChunkAge", "-1", -1, 9999,
                            "In-game days before unvisited unclaimed chunks reset to original state. -1 never resets."),
                        NumberField("SaveDataLimit", "-1", -1, 100000,
                            "Max MB the save can consume before oldest chunks get pruned. -1 disables the limit."),
                        NumberField("BedrollExpiryTime", "45", 1, 365,
                            "Real-world days a bedroll stays active after the owner stops logging in. Expires into a normal block."),
                        NumberField("BedrollDeadZoneSize", "15", 0, 100,
                            "\"Safe zone\" radius around an active bedroll — zombies won't spawn inside this bubble. Smaller = harder."),
                        NumberField("MaxUncoveredMapChunksPerPlayer", "131072", 0, 1000000,
                            "Hard limit on map discovery per player (affects save file size). Vanilla default covers ~32 km²."),
                        BoolField("DynamicMeshEnabled", "true",
                            "Tracks block changes and resolves mesh collisions — needed for vehicle collision in modified areas."),
                        BoolField("DynamicMeshLandClaimOnly", "true",
                            "Only run dynamic mesh inside player LCB areas. Saves CPU."),
                        NumberField("DynamicMeshLandClaimBuffer", "3", 0, 10,
                            "Extra chunk radius around claims included in dynamic mesh."),
                        NumberField("DynamicMeshMaxItemCache", "3", 1, 20,
                            "Concurrent dynamic-mesh items processed. Higher uses more RAM."),
                        NumberField("TwitchServerPermission", "90", 0, 1000,
                            "Permission level required to use Twitch integration. 90 is most trusted players + admins."),
                        BoolField("TwitchBloodMoonAllowed", "false",
                            "Allow Twitch chat actions during blood moon. Off by default — can spiral into lag quickly."),
                    }
                },
            };

            // Flag the 3.0 sandbox-governed fields so the editor can hide them on a 3.0
            // server (see SandboxGovernedKeys for how this set was derived).
            foreach (var g in groups)
                foreach (var f in g.Fields)
                    f.SandboxGoverned = SandboxGovernedKeys.Contains(f.Key);
            return groups;
        }

        /// <summary>
        /// serverconfig.xml properties that 7D2D 3.0 moved into the in-game Sandbox
        /// (governed by the SandboxCode property). On a 3.0 server these are read from the
        /// SandboxCode, NOT from serverconfig.xml, so the editor hides them once a 3.0
        /// server is detected.
        ///
        /// Derived authoritatively from the game, NOT from patch notes: each key here is an
        /// EnumGamePrefs member whose name also exists in the SandboxOptions enum of the 3.0
        /// Assembly-CSharp — which is exactly the game's own sandbox-link test
        /// (GamePrefs.SetupSandboxReferences does Enum.TryParse&lt;SandboxOptions&gt;(prefName)).
        /// Verified against the 3.0 "Dead Hot Summer" server build (2026-06).
        ///
        /// Note the non-obvious SURVIVORS that stay in serverconfig and are deliberately NOT
        /// listed: GameDifficulty, BlockDamagePlayer, EnemyDifficulty, MaxSpawnedZombies,
        /// MaxSpawnedAnimals, LootAbundance, all LandClaim*, PlayerSafeZone*,
        /// BedrollDeadZoneSize. (e.g. BlockDamageAI/AIBM move but BlockDamagePlayer stays;
        /// LootRespawnDays moves but LootAbundance stays.)
        /// </summary>
        private static readonly HashSet<string> SandboxGovernedKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "DeathPenalty", "DropOnDeath", "DropOnQuit", "DayNightLength", "DayLightLength",
            "QuestProgressionDailyLimit", "JarRefund", "BiomeProgression", "StormFreq",
            "BlockDamageAI", "BlockDamageAIBM", "XPMultiplier", "EnemySpawnMode",
            "ZombieFeralSense", "ZombieMove", "ZombieMoveNight", "ZombieFeralMove",
            "ZombieBMMove", "AISmellMode", "BloodMoonFrequency", "BloodMoonRange",
            "BloodMoonWarning", "BloodMoonEnemyCount", "LootRespawnDays", "AirDropFrequency",
            "AirDropMarker",
        };

        /// <summary>
        /// The 3.0 sandbox-governed property keys, for the serverconfig.xml 3.0 migration
        /// in <see cref="ServerConfigService.MigrateConfigTo30"/>. Read-only — the set is
        /// derived authoritatively from the game (see <see cref="SandboxGovernedKeys"/>).
        /// </summary>
        public static System.Collections.Generic.IReadOnlyCollection<string> GetSandboxGovernedKeys()
            => SandboxGovernedKeys;

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

        private static string[] DamagePercentLabels()
            => new[]
            {
                "25% — quarter",
                "50% — halved",
                "75% — easier",
                "100% — vanilla",
                "125% — gentle boost",
                "150% — noticeable",
                "175% — generous",
                "200% — double",
                "300% — speedrun",
            };

        private static string[] ZombieMoveOptions()
            => new[] { "0", "1", "2", "3", "4" };

        private static string[] ZombieMoveLabelOptions()
            => new[] { "Walk", "Jog", "Run", "Sprint", "Nightmare" };
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
        // True when 7D2D 3.0 governs this setting via SandboxCode; the editor hides it on 3.0.
        public bool SandboxGoverned { get; set; }
        public string DefaultValue { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }
        public string[] Options { get; set; }
        public string[] Labels { get; set; }
        public string Description { get; set; }
    }
}
