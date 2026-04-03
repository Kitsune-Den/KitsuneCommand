using System;
using System.IO;
using System.Linq;
using System.Reflection;

class PaintDiagnostic2
{
    static void Main(string[] args)
    {
        var dllPath = args.Length > 0
            ? args[0]
            : Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "src", "KitsuneCommand", "7dtd-binaries", "Assembly-CSharp.dll"
            ));

        var dllDir = Path.GetDirectoryName(dllPath);
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            var name = new AssemblyName(e.Name).Name + ".dll";
            var candidate = Path.Combine(dllDir, name);
            if (File.Exists(candidate))
                try { return Assembly.LoadFrom(candidate); } catch { }
            return null;
        };

        Assembly asm;
        try { asm = Assembly.LoadFrom(dllPath); }
        catch (Exception ex) { Console.WriteLine($"[ERROR] {ex.Message}"); return; }

        Type[] allTypes;
        try { allTypes = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t != null).ToArray();
            Console.WriteLine($"[WARN] Partial load: {allTypes.Length} types loaded.\n");
        }

        // -----------------------------------------------------------------------
        // Full dump of NetPackageSetBlockTexture methods + IL
        // -----------------------------------------------------------------------
        var target = allTypes.FirstOrDefault(t => t.Name == "NetPackageSetBlockTexture");
        if (target == null) { Console.WriteLine("NetPackageSetBlockTexture not found!"); return; }

        Console.WriteLine($"=== {target.FullName} ===\n");
        Console.WriteLine($"Base: {target.BaseType?.FullName}");
        Console.WriteLine($"Interfaces: {string.Join(", ", target.GetInterfaces().Select(i => i.Name))}\n");

        Console.WriteLine("--- FIELDS ---");
        foreach (var f in target.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            var note = f.FieldType == typeof(byte) ? " <<< BYTE" :
                       f.FieldType == typeof(ushort) ? " <<< USHORT" :
                       f.FieldType == typeof(int) ? " (int)" : "";
            Console.WriteLine($"  {f.FieldType.Name,-20} {f.Name}{note}");
        }

        Console.WriteLine("\n--- METHODS ---");
        foreach (var m in target.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Console.WriteLine($"  {m.ReturnType.Name,-15} {m.Name}({parms})");
        }

        // -----------------------------------------------------------------------
        // Also check ALL NetPackage* types for byte fields (pattern across packets)
        // -----------------------------------------------------------------------
        Console.WriteLine("\n\n=== ALL NetPackage* TYPES WITH BYTE/USHORT FIELDS ===\n");
        var netPackageTypes = allTypes
            .Where(t => t.Name.StartsWith("NetPackage"))
            .OrderBy(t => t.Name)
            .ToList();

        Console.WriteLine($"Total NetPackage types: {netPackageTypes.Count}\n");

        foreach (var type in netPackageTypes)
        {
            FieldInfo[] fields;
            try { fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static); }
            catch { continue; }

            var byteFields = fields.Where(f => {
                try { return f.FieldType == typeof(byte) || f.FieldType == typeof(ushort); }
                catch { return false; }
            }).ToList();

            if (byteFields.Any())
            {
                Console.WriteLine($"[{type.Name}]");
                foreach (var f in byteFields)
                {
                    var note = f.FieldType == typeof(byte) ? " <<< BYTE" : " <<< USHORT";
                    Console.WriteLine($"  {f.FieldType.Name,-10} {f.Name}{note}");
                }
            }
        }

        // -----------------------------------------------------------------------
        // BlockTextureData - full dump
        // -----------------------------------------------------------------------
        Console.WriteLine("\n\n=== BlockTextureData FULL DUMP ===\n");
        var btd = allTypes.FirstOrDefault(t => t.Name == "BlockTextureData");
        if (btd != null)
        {
            foreach (var f in btd.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                var note = f.FieldType == typeof(byte) ? " <<< BYTE" :
                           f.FieldType == typeof(ushort) ? " <<< USHORT" :
                           f.FieldType == typeof(int) ? " (int)" : "";
                Console.WriteLine($"  {f.FieldType.Name,-20} {f.Name}{note}");
            }
            Console.WriteLine("\nMethods:");
            foreach (var m in btd.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Console.WriteLine($"  {m.ReturnType.Name,-15} {m.Name}({parms})");
            }
        }

        Console.WriteLine("\nDone.");
    }
}
