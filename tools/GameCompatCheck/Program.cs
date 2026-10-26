// Binary-compatibility check: does a KC build, compiled against the V2.5
// reference assemblies, still bind on another 7D2D version?
//
// The compiler can't answer that. A field that became a property, or a method
// that gained an optional parameter, still compiles from source, but the IL KC
// shipped names the old member and the game throws MissingFieldException /
// MissingMethodException at JIT time. This resolves every type and member
// reference our DLLs make into the game's assemblies against a real install
// and lists the ones that no longer exist.
//
//   dotnet run --project tools/GameCompatCheck -- <7d2d-install> [mod.dll ...]
//
// With no DLLs it checks the Release outputs of every mod in src/.
// Exit code 0 = everything binds, 1 = something doesn't, 2 = bad arguments.

using Mono.Cecil;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: GameCompatCheck <7d2d-install> [mod.dll ...]");
    return 2;
}

var install = args[0];
var managed = Directory.GetDirectories(install, "*_Data")
    .Select(d => Path.Combine(d, "Managed"))
    .FirstOrDefault(Directory.Exists);
if (managed == null)
{
    Console.Error.WriteLine($"No <game>_Data/Managed folder under {install}");
    return 2;
}

var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var mods = args.Length > 1
    ? args.Skip(1).ToArray()
    : new[] { "KitsuneCommand", "KitsunePvPBalance", "KitsuneTraderUnlock", "KitsuneJoinDiag", "KitsunePackRelayLauncher" }
        .Select(m => Path.Combine(repo, "src", m, "bin", "Release", m + ".dll"))
        .ToArray();

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managed);
var harmonyDir = Path.Combine(install, "Mods", "0_TFP_Harmony");
if (Directory.Exists(harmonyDir)) resolver.AddSearchDirectory(harmonyDir);

// Only references into the game's own assemblies; framework/NuGet refs aren't
// what changes between game builds.
var gameAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Assembly-CSharp", "Assembly-CSharp-firstpass", "LogLibrary",
    "UnityEngine", "UnityEngine.CoreModule", "0Harmony", "LiteNetLib",
};

bool IsGame(TypeReference type)
{
    while (type is TypeSpecification spec) type = spec.ElementType;
    return type.Scope is AssemblyNameReference scope && gameAssemblies.Contains(scope.Name);
}

bool Resolves(Func<object> resolve)
{
    try { return resolve() != null; }
    catch (AssemblyResolutionException) { return false; }
}

Console.WriteLine($"Game: {managed}");
int unresolved = 0;
foreach (var path in mods)
{
    if (!File.Exists(path))
    {
        Console.WriteLine($"== {Path.GetFileName(path)}: not built, skipped ({path})");
        continue;
    }

    using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
    Console.WriteLine($"== {Path.GetFileName(path)}");
    var seen = new HashSet<string>();

    foreach (var type in module.GetTypeReferences())
    {
        if (!IsGame(type) || !seen.Add("T:" + type.FullName)) continue;
        if (!Resolves(type.Resolve)) { Console.WriteLine($"  missing type   {type.FullName}"); unresolved++; }
    }

    foreach (var member in module.GetMemberReferences())
    {
        if (!IsGame(member.DeclaringType) || !seen.Add("M:" + member.FullName)) continue;
        bool ok = member switch
        {
            MethodReference method => Resolves(method.Resolve),
            FieldReference field => Resolves(field.Resolve),
            _ => true,
        };
        if (!ok) { Console.WriteLine($"  missing member {member.FullName}"); unresolved++; }
    }

    var patches = module.GetTypes().Where(t => t.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch")).ToList();
    foreach (var patch in patches)
        unresolved += CheckHarmonyPatch(patch);
    if (patches.Count > 0) Console.WriteLine($"  {patches.Count} Harmony patch(es) checked");
}

Console.WriteLine(unresolved == 0
    ? "OK: every game reference resolves and every Harmony patch binds."
    : $"{unresolved} game reference(s) or Harmony patch(es) don't bind on this version.");
return unresolved == 0 ? 0 : 1;

// Harmony patches name their target by string and inject arguments by
// parameter name, so neither shows up as a member reference above. Resolve
// each [HarmonyPatch] class's target the way PatchAll does (declaring type,
// name, optional argument types) and check every Prefix/Postfix parameter
// still names something the target has.
int CheckHarmonyPatch(TypeDefinition patch)
{
    var attrs = patch.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch").ToList();

    TypeReference declaring = null;
    string name = null;
    TypeReference[] argTypes = null;
    foreach (var arg in attrs.SelectMany(a => a.ConstructorArguments))
    {
        if (arg.Value is TypeReference t && declaring == null) declaring = t;
        else if (arg.Value is string s) name = s;
        else if (arg.Value is CustomAttributeArgument[] arr)
            argTypes = arr.Select(x => (TypeReference)x.Value).ToArray();
        else if (arg.Value is TypeReference extra)
            argTypes = (argTypes ?? Array.Empty<TypeReference>()).Append(extra).ToArray();
    }

    var label = $"{patch.FullName} -> {declaring?.Name}.{name}";
    if (patch.Methods.Any(m => m.Name == "TargetMethod"))
    {
        Console.WriteLine($"  patch          {label}: picks its target in code, not checked");
        return 0;
    }
    if (declaring == null || name == null)
    {
        Console.WriteLine($"  patch          {label}: couldn't read the target from its attributes");
        return 1;
    }

    TypeDefinition target;
    try { target = declaring.Resolve(); } catch (AssemblyResolutionException) { target = null; }
    if (target == null)
    {
        Console.WriteLine($"  missing patch  {label}: type not found");
        return 1;
    }

    // AccessTools.Method walks up the base types until a name (and signature) matches.
    var candidates = new List<MethodDefinition>();
    for (var t = target; t != null && candidates.Count == 0; t = SafeResolve(t.BaseType))
        candidates.AddRange(t.Methods.Where(m => m.Name == name && (argTypes == null || SameArgs(m, argTypes))));
    if (candidates.Count != 1)
    {
        var why = candidates.Count == 0 ? "no matching method" : $"{candidates.Count} overloads match, Harmony can't pick";
        Console.WriteLine($"  missing patch  {label}: {why}");
        return 1;
    }

    var original = candidates[0];
    int bad = 0;
    var hooks = patch.Methods.Where(m =>
        m.Name is "Prefix" or "Postfix" or "Finalizer" ||
        m.CustomAttributes.Any(a => a.AttributeType.Name is "HarmonyPrefix" or "HarmonyPostfix" or "HarmonyFinalizer"));
    foreach (var hook in hooks)
    {
        foreach (var p in hook.Parameters)
        {
            var problem = InjectionProblem(p, original);
            if (problem == null) continue;
            Console.WriteLine($"  bad patch      {label}: {hook.Name}({p.Name}) {problem}");
            bad++;
        }
    }
    return bad;
}

string InjectionProblem(ParameterDefinition p, MethodDefinition original)
{
    var n = p.Name;
    if (n is "__instance" or "__state" or "__args" or "__originalMethod" or "__runOriginal" or "__exception") return null;
    if (n == "__result")
        return SameType(p.ParameterType, original.ReturnType) ? null : $"is {Plain(p.ParameterType)}, original returns {original.ReturnType.FullName}";
    if (n.StartsWith("___"))
    {
        var field = n.Substring(3);
        for (var t = original.DeclaringType; t != null; t = SafeResolve(t.BaseType))
            if (t.Fields.Any(f => f.Name == field)) return null;
        return $"field {field} not found on {original.DeclaringType.Name}";
    }
    if (n.StartsWith("__") && int.TryParse(n.Substring(2), out var index))
        return index < original.Parameters.Count ? null : $"original has only {original.Parameters.Count} parameter(s)";

    var match = original.Parameters.FirstOrDefault(op => op.Name == n);
    if (match == null)
        return $"no parameter of that name (original has: {string.Join(", ", original.Parameters.Select(op => op.Name))})";
    return SameType(p.ParameterType, match.ParameterType) ? null : $"is {Plain(p.ParameterType)}, original's is {Plain(match.ParameterType)}";
}

bool SameArgs(MethodDefinition m, TypeReference[] argTypes) =>
    m.Parameters.Count == argTypes.Length &&
    m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(argTypes.Select(a => a.FullName));

// Harmony lets a patch take a by-ref parameter by value and vice versa.
bool SameType(TypeReference a, TypeReference b) => Plain(a) == Plain(b);

string Plain(TypeReference t) => (t is ByReferenceType r ? r.ElementType : t).FullName;

TypeDefinition SafeResolve(TypeReference t)
{
    try { return t?.Resolve(); } catch (AssemblyResolutionException) { return null; }
}
