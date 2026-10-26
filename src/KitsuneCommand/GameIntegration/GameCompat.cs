using System.Linq.Expressions;
using System.Reflection;

namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// Version-tolerant access to game members whose shape changed between the
    /// 7D2D builds KC supports (V2.5 through V3.3).
    ///
    /// KC compiles against V2.5 reference assemblies, and the IL that produces
    /// binds to the exact member it saw there. When a later build turns a field
    /// into a property, or adds an optional parameter, the source still compiles
    /// but the shipped DLL throws MissingFieldException / MissingMethodException
    /// the first time a method touching that member is JIT-compiled (so a
    /// try/catch inside that method can't catch it). Measured against the real
    /// assemblies:
    ///
    ///   - V3.2: Localization.Get(string, bool) gained a third optional parameter;
    ///           BlockChangeInfo.pos became blockValueRef (a block OR a prop).
    ///   - V3.3: Bag/Inventory.GetSlots() are gone (both sit on an ItemStackGrid);
    ///           ItemStack.itemValue/count and ItemValue.type/Quality/UseTimes
    ///           became properties. ItemClass gained MaxCount (stack size after
    ///           the server's stack multiplier) and a static MaxQualityTier.
    ///   - V3.3: EnumGamePrefs grew from 286 to 317 members, so every member from
    ///           #168 on moved. KC's IL holds the V2.5 number, not the name, so
    ///           GamePrefs.GetInt(EnumGamePrefs.ServerVisibility) read ServerEnabled
    ///           (a bool) on 3.3+. Nothing fails to bind; it reads the wrong pref.
    ///           Name game prefs and stats through Pref/Stat below.
    ///
    /// Every accessor here is bound once, by name, to whatever the running game
    /// has, and compiled to a delegate so the hot paths (block changes) don't pay
    /// reflection per call. Anything touching these members goes through here.
    /// </summary>
    public static class GameCompat
    {
        private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;

        // Lazy, so a member that fails to bind only breaks its own callers.
        private static readonly Lazy<Func<ItemStack, ItemValue>> _stackItemValue = Bind(() => Getter<ItemStack, ItemValue>("itemValue"));
        private static readonly Lazy<Func<ItemStack, int>> _stackCount = Bind(() => Getter<ItemStack, int>("count"));
        private static readonly Lazy<Func<ItemValue, int>> _valueType = Bind(() => Getter<ItemValue, int>("type"));
        private static readonly Lazy<Func<ItemValue, ushort>> _valueQuality = Bind(() => Getter<ItemValue, ushort>("Quality"));
        private static readonly Lazy<Action<ItemValue, ushort>> _setValueQuality = Bind(() => Setter<ItemValue, ushort>("Quality"));
        private static readonly Lazy<Func<ItemValue, float>> _valueUseTimes = Bind(() => Getter<ItemValue, float>("UseTimes"));
        private static readonly Lazy<Func<Bag, ItemStack[]>> _bagSlots = Bind(SlotsReader<Bag>);
        private static readonly Lazy<Func<Inventory, ItemStack[]>> _inventorySlots = Bind(SlotsReader<Inventory>);
        private static readonly Lazy<Func<string, string>> _localize = Bind(BuildLocalize);
        private static readonly Lazy<Func<BlockChangeInfo, World, Vector3i>> _blockChangePos = Bind(BuildBlockChangePos);
        private static readonly Lazy<Func<ItemClass, int>> _stackLimit = Bind(BuildStackLimit);
        private static readonly Lazy<Func<int>> _maxQualityTier = Bind(BuildMaxQualityTier);

        public static ItemValue GetItemValue(ItemStack stack) => _stackItemValue.Value(stack);
        public static int GetCount(ItemStack stack) => _stackCount.Value(stack);
        public static int GetItemType(ItemValue value) => _valueType.Value(value);
        public static ushort GetQuality(ItemValue value) => _valueQuality.Value(value);
        public static void SetQuality(ItemValue value, ushort quality) => _setValueQuality.Value(value, quality);
        public static float GetUseTimes(ItemValue value) => _valueUseTimes.Value(value);

        /// <summary>Snapshot of the player's backpack slots, in slot order. Entries may be null.</summary>
        public static ItemStack[] GetSlots(Bag bag) => _bagSlots.Value(bag);

        /// <summary>Snapshot of the player's toolbelt slots, in slot order. Entries may be null.</summary>
        public static ItemStack[] GetSlots(Inventory inventory) => _inventorySlots.Value(inventory);

        /// <summary>Localization.Get(key) with the game's default arguments.</summary>
        public static string Localize(string key) => _localize.Value(key);

        /// <summary>
        /// The block position a change targets. On V3.2+ a change can target a
        /// prop instead of a block; that resolves to the block the prop sits in,
        /// the same way the game's own BlockValueRef.ToBlockPos does.
        /// </summary>
        public static Vector3i GetBlockPos(BlockChangeInfo change, World world) => _blockChangePos.Value(change, world);

        /// <summary>
        /// The largest legal stack of this item: ItemClass.MaxCount where the game
        /// has it (V3.3, which applies the server's stack-size multiplier), else
        /// Stacknumber.
        /// </summary>
        public static int GetStackLimit(ItemClass itemClass) => _stackLimit.Value(itemClass);

        /// <summary>ItemClass.MaxQualityTier on builds that have it; 0 where it doesn't exist or isn't set yet.</summary>
        public static int GetMaxQualityTier() => _maxQualityTier.Value();

        /// <summary>
        /// A game pref as the running game numbers it. Pass the name with nameof
        /// (<c>Pref(nameof(EnumGamePrefs.ServerVisibility))</c>) so it's still
        /// compiler-checked; writing <c>EnumGamePrefs.X</c> directly ships V2.5's
        /// number, which names a different pref on later builds.
        /// </summary>
        public static EnumGamePrefs Pref(string name) => ByName<EnumGamePrefs>(name);

        /// <summary>A game stat as the running game numbers it. See <see cref="Pref"/>.</summary>
        public static EnumGameStats Stat(string name) => ByName<EnumGameStats>(name);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, string), object> _enumByName =
            new System.Collections.Concurrent.ConcurrentDictionary<(Type, string), object>();

        // typeof(T) is the running game's enum, so Enum.Parse returns its number for the name.
        private static T ByName<T>(string name) where T : struct =>
            (T)_enumByName.GetOrAdd((typeof(T), name), key =>
                Enum.IsDefined(key.Item1, key.Item2)
                    ? Enum.Parse(key.Item1, key.Item2)
                    : throw new MissingMemberException(key.Item1.Name, key.Item2));

        // --- binders ---

        private static Lazy<T> Bind<T>(Func<T> build) => new Lazy<T>(build, LazyThreadSafetyMode.ExecutionAndPublication);

        private static MemberExpression PropertyOrField(Expression instance, string name)
        {
            var type = instance.Type;
            var prop = type.GetProperty(name, InstancePublic);
            if (prop != null) return Expression.Property(instance, prop);
            var field = type.GetField(name, InstancePublic);
            if (field != null) return Expression.Field(instance, field);
            throw new MissingMemberException(type.Name, name);
        }

        private static Func<TObj, TVal> Getter<TObj, TVal>(string name)
        {
            var obj = Expression.Parameter(typeof(TObj), "obj");
            return Expression.Lambda<Func<TObj, TVal>>(PropertyOrField(obj, name), obj).Compile();
        }

        private static Action<TObj, TVal> Setter<TObj, TVal>(string name)
        {
            var obj = Expression.Parameter(typeof(TObj), "obj");
            var value = Expression.Parameter(typeof(TVal), "value");
            return Expression.Lambda<Action<TObj, TVal>>(
                Expression.Assign(PropertyOrField(obj, name), value), obj, value).Compile();
        }

        /// <summary>
        /// GetSlots() up to V3.2; on V3.3 (where it's gone) PerformActionOnSlots,
        /// which both Bag and Inventory override to visit every slot in order.
        /// </summary>
        private static Func<T, ItemStack[]> SlotsReader<T>() where T : class
        {
            var getSlots = typeof(T).GetMethod("GetSlots", InstancePublic, null, Type.EmptyTypes, null);
            if (getSlots != null && getSlots.ReturnType == typeof(ItemStack[]))
                return (Func<T, ItemStack[]>)Delegate.CreateDelegate(typeof(Func<T, ItemStack[]>), getSlots);

            var perform = typeof(T).GetMethod("PerformActionOnSlots", InstancePublic, null,
                new[] { typeof(Action<ItemStack>) }, null);
            if (perform == null)
                throw new MissingMethodException(typeof(T).Name, "GetSlots");

            var visit = (Action<T, Action<ItemStack>>)Delegate.CreateDelegate(
                typeof(Action<T, Action<ItemStack>>), perform);
            return container =>
            {
                var slots = new List<ItemStack>();
                visit(container, slots.Add);
                return slots.ToArray();
            };
        }

        /// <summary>
        /// Localization.Get's first parameter is always the key; the rest
        /// (case-insensitive flag, then a language name on V3.2+) are optional,
        /// so pass each one's own default.
        /// </summary>
        private static Func<string, string> BuildLocalize()
        {
            var get = typeof(Localization).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Where(m => m.Name == "Get" && m.ReturnType == typeof(string))
                .Select(m => new { Method = m, Params = m.GetParameters() })
                .Where(x => x.Params.Length >= 1 && x.Params[0].ParameterType == typeof(string)
                            && x.Params.Skip(1).All(p => p.IsOptional))
                .OrderBy(x => x.Params.Length)
                .FirstOrDefault()
                ?? throw new MissingMethodException(nameof(Localization), "Get");

            var key = Expression.Parameter(typeof(string), "key");
            var args = new List<Expression> { key };
            args.AddRange(get.Params.Skip(1).Select(p => (Expression)Expression.Constant(p.DefaultValue, p.ParameterType)));
            return Expression.Lambda<Func<string, string>>(Expression.Call(get.Method, args), key).Compile();
        }

        private static Func<ItemClass, int> BuildStackLimit()
        {
            var maxCount = typeof(ItemClass).GetProperty("MaxCount", InstancePublic);
            if (maxCount != null && maxCount.PropertyType == typeof(int))
                return Getter<ItemClass, int>("MaxCount");
            return ic => ic.Stacknumber.Value;
        }

        private static Func<int> BuildMaxQualityTier()
        {
            var field = typeof(ItemClass).GetField("MaxQualityTier", BindingFlags.Static | BindingFlags.Public);
            if (field == null || field.FieldType != typeof(int)) return () => 0;
            return Expression.Lambda<Func<int>>(Expression.Field(null, field)).Compile();
        }

        /// <summary>
        /// BlockChangeInfo.pos up to V3.1; blockValueRef.ToBlockPos(world) from V3.2.
        /// </summary>
        private static Func<BlockChangeInfo, World, Vector3i> BuildBlockChangePos()
        {
            var change = Expression.Parameter(typeof(BlockChangeInfo), "change");
            var world = Expression.Parameter(typeof(World), "world");

            var pos = typeof(BlockChangeInfo).GetField("pos", InstancePublic);
            Expression body;
            if (pos != null && pos.FieldType == typeof(Vector3i))
            {
                body = Expression.Field(change, pos);
            }
            else
            {
                var bvRef = typeof(BlockChangeInfo).GetField("blockValueRef", InstancePublic)
                    ?? throw new MissingFieldException(nameof(BlockChangeInfo), "pos");
                var toBlockPos = bvRef.FieldType.GetMethods(InstancePublic)
                    .FirstOrDefault(m => m.Name == "ToBlockPos" && m.ReturnType == typeof(Vector3i)
                                         && m.GetParameters().Length == 1
                                         && m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(World)))
                    ?? throw new MissingMethodException(bvRef.FieldType.Name, "ToBlockPos");
                body = Expression.Call(Expression.Field(change, bvRef), toBlockPos,
                    Expression.Convert(world, toBlockPos.GetParameters()[0].ParameterType));
            }
            return Expression.Lambda<Func<BlockChangeInfo, World, Vector3i>>(body, change, world).Compile();
        }
    }
}
