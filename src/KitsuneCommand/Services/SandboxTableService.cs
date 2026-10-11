using System.Collections;
using System.Globalization;
using System.Reflection;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Reads the 7D2D 3.0+ Sandbox option table out of the running game, so the
    /// config editor can decode and encode a SandboxCode instead of making the
    /// operator build one in-game and paste it back.
    ///
    /// A code is a version letter followed by three-letter triples: two letters
    /// for the option (its position in the SandboxOptions enum) and one for the
    /// value (its position in that option's own value list). Those lists, and
    /// which value is the default, change between game builds under the same
    /// version letter (3.2 prepended "Default" to the item-tier lists, shifting
    /// every letter by one). PackRelay works around that with a table extracted
    /// per game version. KC runs inside the game, so it reads the table from the
    /// game itself and is always exactly right for the build it is running on,
    /// including any user or modded presets.
    ///
    /// All of it is reflection: KC compiles against V2.5 reference assemblies,
    /// which predate the Sandbox types. The manager is set up by GameEntrypoint
    /// before mods load, and nothing here changes it — LoadOptionsFromCode et al.
    /// mutate the live options, so they are never called.
    /// </summary>
    public class SandboxTableService
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly object _lock = new object();
        private SandboxTable _table;

        /// <summary>The table, the code the running world started with, or why there is none.</summary>
        public SandboxConfigResponse GetSandboxConfig()
        {
            var managerType = FindGameType("SandboxOptions.SandboxOptionManager");
            if (managerType == null)
                return new SandboxConfigResponse { Supported = false };

            try
            {
                return new SandboxConfigResponse
                {
                    Supported = true,
                    Table = GetTable(managerType),
                    LiveCode = ReadLiveCode()
                };
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Log.Warning($"[KitsuneCommand] Couldn't read the Sandbox option table: {inner.GetType().Name}: {inner.Message}");
                return new SandboxConfigResponse { Supported = true, Error = inner.Message };
            }
        }

        private SandboxTable GetTable(Type managerType)
        {
            lock (_lock)
            {
                // Fixed for the life of the process: options are registered at
                // startup and presets load once. Failures aren't cached.
                if (_table == null)
                    _table = BuildTable(GetStatic(managerType, "Current"));
                return _table;
            }
        }

        /// <summary>
        /// Builds the table from an initialized SandboxOptionManager. Public so it
        /// can be exercised against the real game assembly outside the game.
        /// </summary>
        public static SandboxTable BuildTable(object manager)
        {
            if (manager == null) throw new InvalidOperationException("SandboxOptionManager.Current is null.");
            var managerType = manager.GetType();

            var table = new SandboxTable
            {
                GameVersion = Constants.cVersionInformation.LongString,
                CodeVersion = Convert.ToString(GetStatic(managerType, "currentVersion"), CultureInfo.InvariantCulture)
            };

            // OptionsByCategory is a DictionaryList: .list holds each category's
            // options in registration order, categories in first-seen order. The
            // game's SaveCurrentToPreset walks it the same way when writing a code.
            var byCategory = (IEnumerable)Get(Get(manager, "OptionsByCategory"), "list");
            foreach (IEnumerable options in byCategory)
            {
                string category = null;
                foreach (var option in options)
                {
                    var info = ReadOption(option);
                    if (category == null)
                    {
                        category = info.Category;
                        table.Categories.Add(new SandboxCategoryInfo
                        {
                            Name = category,
                            // The tab label, as XUiC_SandboxOptions resolves it ("General" shows as "Player").
                            Label = LocalizeOrNull("sandboxOptionCategory" + category) ?? category
                        });
                    }
                    table.Options.Add(info);
                }
            }

            foreach (var preset in (IEnumerable)Get(manager, "SandboxPresets"))
            {
                if ((bool)Get(preset, "IsCustomPreset")) continue; // the "Custom" placeholder, no settings
                table.Presets.Add(ReadPreset(preset));
            }

            return table;
        }

        private static SandboxOptionInfo ReadOption(object option)
        {
            var valueSet = Call(option, "GetValueSet");
            int count = (int)Call(valueSet, "GetValueCount");

            var info = new SandboxOptionInfo
            {
                Index = Convert.ToInt32(Get(option, "Option"), CultureInfo.InvariantCulture),
                Name = Get(option, "Option").ToString(),
                Label = GameText((string)Get(option, "OptionNameText")),
                Description = GameText((string)Get(option, "DescriptionText")),
                Category = (string)Get(option, "CategoryName"),
                Kind = Get(option, "OptionType").ToString().ToLowerInvariant(),
                DefaultIndex = (int)Call(option, "GetDefaultIndex")
            };

            var raw = new object[count];
            for (int i = 0; i < count; i++)
            {
                raw[i] = ValueAt(valueSet, i);
                info.Values.Add(new SandboxValueInfo
                {
                    Label = (string)Call(valueSet, "GetDisplayAtIndex", i),
                    Value = Convert.ToString(raw[i], CultureInfo.InvariantCulture)
                });
            }

            // SandboxOptionInt/Float/Boolean.DisabledOptions: at Value (or at any
            // other value when Inverted) the listed options are switched off — the
            // same test SandboxOptionManager.SetOption applies.
            var disabled = TryGet(option, "DisabledOptions");
            if (disabled != null)
            {
                var at = Get(disabled, "Value");
                bool inverted = (bool)Get(disabled, "Inverted");
                var disables = new SandboxDisablesInfo();
                foreach (var target in (IEnumerable)Get(disabled, "DisabledOptions"))
                    disables.Options.Add(target.ToString());
                for (int i = 0; i < count; i++)
                    if (SameValue(raw[i], at) != inverted)
                        disables.WhenValueIndices.Add(i);
                if (disables.Options.Count > 0 && disables.WhenValueIndices.Count > 0)
                    info.Disables = disables;
            }

            return info;
        }

        private static SandboxPresetInfo ReadPreset(object preset)
        {
            var group = (string)Get(preset, "Group") ?? "";
            var info = new SandboxPresetInfo
            {
                Name = (string)Get(preset, "Name"),
                Label = GameText((string)Get(preset, "DisplayName")),
                Description = GameText((string)Get(preset, "DisplayDescription")),
                Group = group,
                GroupLabel = LocalizeOrNull("sandboxPresetGroup" + group) ?? group,
                Rating = Convert.ToInt32(Get(preset, "DifficultyRating"), CultureInfo.InvariantCulture),
                IsDefault = (bool)Get(preset, "IsDefault"),
                IsUser = (bool)Get(preset, "IsUserPreset"),
                IsModded = (bool)Get(preset, "IsModded")
            };
            foreach (DictionaryEntry entry in (IDictionary)Get(preset, "PresetValues"))
                info.Values[entry.Key.ToString()] = (int)entry.Value;
            return info;
        }

        /// <summary>
        /// GameStats.SandboxCode: what the running world was started with. The
        /// server copies serverconfig's SandboxCode into it on every start.
        /// </summary>
        private static string ReadLiveCode()
        {
            try
            {
                if (Array.IndexOf(Enum.GetNames(typeof(EnumGameStats)), "SandboxCode") < 0) return null;
                return GameStats.GetString((EnumGameStats)Enum.Parse(typeof(EnumGameStats), "SandboxCode"));
            }
            catch
            {
                return null; // no world loaded yet
            }
        }

        /// <summary>
        /// Localization.csv writes line breaks as a literal backslash-n; make sure
        /// the panel gets real ones whether or not the game's loader already did that.
        /// </summary>
        private static string GameText(string text)
        {
            return (text ?? "").Replace("\\n", "\n").Trim();
        }

        // --- reflection helpers ---

        private static Type FindGameType(string fullName)
        {
            return typeof(GameManager).Assembly.GetType(fullName, false);
        }

        private static object ValueAt(object valueSet, int index)
        {
            var args = new object[] { index, null };
            valueSet.GetType().GetMethod("GetValue", Instance, null,
                new[] { typeof(int), typeof(object).MakeByRefType() }, null).Invoke(valueSet, args);
            return args[1];
        }

        private static bool SameValue(object a, object b)
        {
            if (a == null || b == null) return a == b;
            if (a is bool || b is bool) return a.Equals(b);
            // Int sets compare ints, float sets floats; compare as double so a
            // boxed int and float of the same value still match.
            return Convert.ToDouble(a, CultureInfo.InvariantCulture) == Convert.ToDouble(b, CultureInfo.InvariantCulture);
        }

        /// <summary>Localization.Get(key) when the key exists, else null.</summary>
        private static string LocalizeOrNull(string key)
        {
            var exists = typeof(Localization).GetMethod("Exists", Static, null, new[] { typeof(string) }, null);
            if (exists != null)
                return (bool)exists.Invoke(null, new object[] { key }) ? GameIntegration.GameCompat.Localize(key) : null;
            var text = GameIntegration.GameCompat.Localize(key);
            return string.IsNullOrEmpty(text) || text == key ? null : text;
        }

        private static object Get(object target, string name)
        {
            var type = target.GetType();
            var prop = type.GetProperty(name, Instance);
            if (prop != null) return prop.GetValue(target, null);
            var field = type.GetField(name, Instance);
            if (field != null) return field.GetValue(target);
            throw new MissingMemberException(type.Name, name);
        }

        private static object TryGet(object target, string name)
        {
            var type = target.GetType();
            var prop = type.GetProperty(name, Instance);
            if (prop != null) return prop.GetValue(target, null);
            return type.GetField(name, Instance)?.GetValue(target);
        }

        private static object GetStatic(Type type, string name)
        {
            var prop = type.GetProperty(name, Static);
            if (prop != null) return prop.GetValue(null, null);
            var field = type.GetField(name, Static);
            if (field != null) return field.GetValue(null);
            throw new MissingMemberException(type.Name, name);
        }

        /// <summary>
        /// Calls the overload taking these arguments, filling any trailing optional
        /// parameters with their defaults (later builds add optional parameters,
        /// e.g. a language name on GetDisplayAtIndex).
        /// </summary>
        private static object Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethods(Instance)
                .Where(m => m.Name == name)
                .Select(m => new { Method = m, Params = m.GetParameters() })
                .Where(x => x.Params.Length >= args.Length && x.Params.Skip(args.Length).All(p => p.IsOptional))
                .OrderBy(x => x.Params.Length)
                .FirstOrDefault()
                ?? throw new MissingMethodException(target.GetType().Name, name);
            var full = args.Concat(method.Params.Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
            return method.Method.Invoke(target, full);
        }
    }
}
