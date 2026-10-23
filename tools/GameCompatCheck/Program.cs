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
    : new[] { "KitsuneCommand", "KitsunePvPBalance", "KitsuneJoinDiag", "KitsunePackRelayLauncher" }
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
}

Console.WriteLine(unresolved == 0 ? "OK: every game reference resolves." : $"{unresolved} game reference(s) don't resolve on this version.");
return unresolved == 0 ? 0 : 1;
