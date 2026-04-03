// PaintDiagnostic.csx
// Run with: dotnet script PaintDiagnostic.csx
// Or compile as a standalone console app.
//
// Loads Assembly-CSharp.dll via reflection and dumps all paint-related types,
// fields, and their underlying types so we can find where the 255 limit comes from.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

var dllPath = Path.GetFullPath(
    Path.Combine(__DIR__, "..", "src", "KitsuneCommand", "7dtd-binaries", "Assembly-CSharp.dll")
);

if (!File.Exists(dllPath))
{
    Console.WriteLine($"[ERROR] DLL not found at: {dllPath}");
    return;
}

Console.WriteLine($"Loading: {dllPath}");
var asm = Assembly.LoadFrom(dllPath);

// Keywords to search for paint-related types
var paintKeywords = new[] { "paint", "Paint", "texture", "Texture", "opaque", "Opaque", "PaintMaterial", "BlockPaint" };

Console.WriteLine("\n=== PAINT-RELATED TYPES ===\n");

var allTypes = asm.GetTypes();
var paintTypes = allTypes
    .Where(t => paintKeywords.Any(k => t.Name.Contains(k)))
    .OrderBy(t => t.Name)
    .ToList();

if (!paintTypes.Any())
{
    Console.WriteLine("No paint-related types found by keyword. Trying broader search...");
    paintTypes = allTypes
        .Where(t => t.Name.ToLower().Contains("paint") || t.Name.ToLower().Contains("texture"))
        .OrderBy(t => t.Name)
        .ToList();
}

foreach (var type in paintTypes)
{
    Console.WriteLine($"\n[TYPE] {type.FullName}");

    // Fields
    var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    foreach (var f in fields)
    {
        var highlight = (f.FieldType == typeof(byte) || f.FieldType == typeof(sbyte)) ? " <<< BYTE!" :
                        (f.FieldType == typeof(ushort) || f.FieldType == typeof(short)) ? " <<< SHORT" :
                        (f.FieldType == typeof(int)) ? " (int)" : "";
        Console.WriteLine($"  [FIELD] {f.FieldType.Name,-15} {f.Name}{highlight}");
    }

    // Properties
    var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    foreach (var p in props)
    {
        var highlight = (p.PropertyType == typeof(byte) || p.PropertyType == typeof(sbyte)) ? " <<< BYTE!" :
                        (p.PropertyType == typeof(ushort) || p.PropertyType == typeof(short)) ? " <<< SHORT" :
                        (p.PropertyType == typeof(int)) ? " (int)" : "";
        Console.WriteLine($"  [PROP]  {p.PropertyType.Name,-15} {p.Name}{highlight}");
    }
}

// Also search for any type that has a field named something like "TextureId", "PaintId", "paintIndex"
Console.WriteLine("\n\n=== ALL TYPES WITH PAINT/TEXTURE ID FIELDS ===\n");

var idKeywords = new[] { "TextureId", "textureId", "PaintId", "paintId", "paintIndex", "textureIndex", "TextureIndex", "PaintIndex", "opaqueId", "OpaqueId" };

foreach (var type in allTypes)
{
    var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    var matchingFields = fields.Where(f => idKeywords.Any(k => f.Name.Contains(k))).ToList();

    if (matchingFields.Any())
    {
        Console.WriteLine($"\n[TYPE] {type.FullName}");
        foreach (var f in matchingFields)
        {
            var highlight = (f.FieldType == typeof(byte) || f.FieldType == typeof(sbyte)) ? " <<< BYTE!" :
                            (f.FieldType == typeof(ushort) || f.FieldType == typeof(short)) ? " <<< SHORT" :
                            (f.FieldType == typeof(int)) ? " (int)" : "";
            Console.WriteLine($"  [FIELD] {f.FieldType.Name,-15} {f.Name}{highlight}");
        }
    }
}

// Dump block-related types that might store paint index per block
Console.WriteLine("\n\n=== BLOCK TYPES WITH BYTE/USHORT FIELDS (potential paint index storage) ===\n");

var blockTypes = allTypes.Where(t => t.Name.StartsWith("Block") || t.Name == "ChunkCluster" || t.Name == "Chunk").ToList();
foreach (var type in blockTypes)
{
    var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    var byteFields = fields.Where(f => f.FieldType == typeof(byte) || f.FieldType == typeof(ushort)).ToList();

    if (byteFields.Any())
    {
        Console.WriteLine($"\n[TYPE] {type.FullName}");
        foreach (var f in byteFields)
        {
            var highlight = f.FieldType == typeof(byte) ? " <<< BYTE!" : " <<< USHORT";
            Console.WriteLine($"  {f.FieldType.Name,-10} {f.Name}{highlight}");
        }
    }
}

Console.WriteLine("\n\nDone.");
