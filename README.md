# DrMod

DrMod is a .NET 8 library for analyzing, validating, and managing Minecraft mods for Forge, NeoForge, Fabric, and Quilt. It provides advanced tools for reading mod metadata, dependency analysis, conflict detection, crash report resolution, and more.

## Features
- Cross-loader support: Forge, NeoForge, Fabric, Quilt
- Read mod metadata from JARs or metadata files
- Detect required/optional dependencies and incompatibilities
- Validate mods and mod folders
- Detect duplicate/conflicting/incompatible mods
- Analyze crash reports to identify problematic mods
- Query mods by loader, Minecraft version, dependencies, and more

## Installation
Add the DrMod project to your solution or reference the DLL. Target .NET 8 or later.

## Usage

### Creating an Analyzer
```
using DrMod;

var analyzer = new DrMod();
```
## API Reference with Examples

### Metadata & Validation Methods

#### `ReadModMetadata(string filePath)`
Read metadata from a mod JAR or metadata file.

**Example:**
```
// Reading from a JAR file
var metadata = analyzer.ReadModMetadata(@"C:\mods\jei-1.20.1-15.2.0.27.jar");
if (metadata != null)
{
    Console.WriteLine($"Mod ID: {metadata.modId}");
    Console.WriteLine($"Name: {metadata.name}");
    Console.WriteLine($"Version: {metadata.modVersion}");
    Console.WriteLine($"Loader: {metadata.loader}");
}

// Reading from a metadata file directly
var fabricMeta = analyzer.ReadModMetadata(@"C:\fabric.mod.json");
var forgeMeta = analyzer.ReadModMetadata(@"C:\META-INF\mods.toml");
```
#### `ReadAllModMetadataInFolder(string folderPath)`
Read metadata for all mods in a folder.

**Example:**
```
var allMods = analyzer.ReadAllModMetadataInFolder(@"C:\Minecraft\mods");
Console.WriteLine($"Found {allMods.Count} mods:");

foreach (var mod in allMods)
{
    Console.WriteLine($"- {mod.name} ({mod.modId}) v{mod.modVersion} [{mod.loader}]");
}

// Filter by specific criteria
var forgeMods = allMods.Where(m => m.loader == "Forge").ToList();
var fabricMods = allMods.Where(m => m.loader == "Fabric").ToList();
```
#### `ValidateMod(string modPath)`
Validate a single mod and return a list of errors.

**Example:**
```
var errors = analyzer.ValidateMod(@"C:\mods\examplemod.jar");

if (errors.Count == 0)
{
    Console.WriteLine("✅ Mod is valid!");
}
else
{
    Console.WriteLine("❌ Mod validation errors:");
    foreach (var error in errors)
    {
        Console.WriteLine($"  - {error}");
    }
}
```

**Example output:**
  ❌ Mod validation errors:
  - Missing modId.
  - Missing Minecraft version.
  - Duplicate required dependency: forge
#### `ValidateModsFolder(string folderPath)`
Validate all mods in a folder and detect folder-wide issues.

**Example:**
```
var folderErrors = analyzer.ValidateModsFolder(@"C:\Minecraft\mods");

if (folderErrors.Count == 0)
{
    Console.WriteLine("✅ All mods in folder are valid!");
}
else
{
    Console.WriteLine($"❌ Found {folderErrors.Count} issues:");
    foreach (var error in folderErrors)
    {
        Console.WriteLine($"  {error}");
    }
}
```
**Example output:**
  ❌ Found 3 issues:
  [badmod.jar] Missing modId.
  Duplicate modId detected: jei
  Multiple Minecraft versions detected in mods folder: 1.20.1, 1.19.4
  [modA] is incompatible with [modB]
### Compatibility & Conflict Detection Methods

#### `IsCompatible(string modPath, string mcVersion, string loader, string loaderVersion)`
Check if a mod is compatible with specific game/loader versions.

**Example:**
```
// Check compatibility with specific versions
bool isCompatible = analyzer.IsCompatible(
    @"C:\mods\jei.jar",
    mcVersion: "1.20.1",
    loader: "Forge",
    loaderVersion: "47.1.0"
);

Console.WriteLine($"JEI is compatible: {isCompatible}");

// Batch check multiple mods
var mods = Directory.GetFiles(@"C:\mods", "*.jar");
var targetMC = "1.20.1";
var targetLoader = "Forge";
var targetLoaderVer = "47.1.0";

foreach (var mod in mods)
{
    var compatible = analyzer.IsCompatible(mod, targetMC, targetLoader, targetLoaderVer);
    var modName = Path.GetFileNameWithoutExtension(mod);
    Console.WriteLine($"{modName}: {(compatible ? "✅" : "❌")}");
}
```
#### `DetectConflicts(List<string> modPaths)`
Detect duplicate mod IDs in a list of mod files.

**Example:**
```
var modFiles = Directory.GetFiles(@"C:\mods", "*.jar").ToList();
var conflicts = analyzer.DetectConflicts(modFiles);

if (conflicts.Count > 0)
{
    Console.WriteLine("🔴 Conflicting mods detected:");
    foreach (var conflict in conflicts)
    {
        Console.WriteLine($"  Duplicate mod ID: {conflict}");
        // Find which files have the same ID
        var conflictingFiles = modFiles.Where(f => 
        {
            var meta = analyzer.ReadModMetadata(f);
            return meta?.modId == conflict;
        });
        foreach (var file in conflictingFiles)
        {
            Console.WriteLine($"    - {Path.GetFileName(file)}");
        }
    }
}
```
#### `DetectIncompatibilities(List<string> modPaths)`
Detect explicit incompatibilities between mods.

**Example:**
```
var modFiles = Directory.GetFiles(@"C:\mods", "*.jar").ToList();
var incompatibilities = analyzer.DetectIncompatibilities(modFiles);

if (incompatibilities.Count > 0)
{
    Console.WriteLine("⚠️ Incompatible mods detected:");
    foreach (var (modId, incompatibleWith) in incompatibilities)
    {
        Console.WriteLine($"  {modId} is incompatible with {incompatibleWith}");
    }
    Console.WriteLine("\n💡 Consider removing one mod from each incompatible pair.");
}
```
### Dependency Analysis Methods

#### `GetRequiredDependencies(string modPath)`
Get required dependencies for a mod.

**Example:**
```
var requiredDeps = analyzer.GetRequiredDependencies(@"C:\mods\jei.jar");

Console.WriteLine("Required dependencies:");
foreach (var dep in requiredDeps)
{
    Console.WriteLine($"  - {dep}");
}
```
**Example output:** Required dependencies:
  - forge
  - minecraft
#### `GetOptionalDependencies(string modPath)`
Get optional dependencies for a mod.

**Example:**var optionalDeps = analyzer.GetOptionalDependencies(@"C:\mods\jei.jar");

Console.WriteLine("Optional dependencies:");
foreach (var dep in optionalDeps)
{
    Console.WriteLine($"  - {dep}");
}
#### `GetIncompatibilities(string modPath)`
Get explicit incompatibilities for a mod.

**Example:**
```
var incompatibilities = analyzer.GetIncompatibilities(@"C:\mods\optifine.jar");

if (incompatibilities.Count > 0)
{
    Console.WriteLine("⚠️ This mod is incompatible with:");
    foreach (var inc in incompatibilities)
    {
        Console.WriteLine($"  - {inc}");
    }
}
```
#### `GetAllDependencies(string modPath, bool includeOptional = false)`
Recursively get all (transitive) dependencies for a mod.

**Example:**
```
// Get only required dependencies (including transitive)
var allRequired = analyzer.GetAllDependencies(@"C:\mods\complexmod.jar", false);

// Get all dependencies including optional ones
var allDeps = analyzer.GetAllDependencies(@"C:\mods\complexmod.jar", true);

Console.WriteLine($"Total required dependencies: {allRequired.Count}");
Console.WriteLine($"Total dependencies (inc. optional): {allDeps.Count}");

Console.WriteLine("\nFull dependency tree:");
foreach (var dep in allDeps)
{
    Console.WriteLine($"  - {dep}");
}
```
#### `GetDependents(string modId, string folderPath)`
Get all mods that depend on a given mod.

**Example:**
```
var dependents = analyzer.GetDependents("jei", @"C:\mods");

Console.WriteLine($"Mods that depend on JEI ({dependents.Count}):");
foreach (var dependent in dependents)
{
    Console.WriteLine($"  - {dependent}");
}

// Useful for understanding impact of removing a mod
if (dependents.Count > 0)
{
    Console.WriteLine($"\n⚠️ Removing JEI will affect {dependents.Count} other mods!");
}
```
#### `GetModsWithMissingDependencies(string folderPath)`
Get mods that have missing required dependencies.

**Example:**
```
var modsWithMissingDeps = analyzer.GetModsWithMissingDependencies(@"C:\mods");

if (modsWithMissingDeps.Count > 0)
{
    Console.WriteLine($"🔴 {modsWithMissingDeps.Count} mods have missing dependencies:");
    foreach (var modId in modsWithMissingDeps)
    {
        var modPath = Directory.GetFiles(@"C:\mods", "*.jar")
            .FirstOrDefault(f => analyzer.ReadModMetadata(f)?.modId == modId);
        if (modPath != null)
        {
            var requiredDeps = analyzer.GetRequiredDependencies(modPath);
            var availableMods = analyzer.ReadAllModMetadataInFolder(@"C:\mods")
                .Select(m => m.modId).ToHashSet();
            var missingDeps = requiredDeps.Where(dep => !availableMods.Contains(dep));
            Console.WriteLine($"  {modId}:");
            foreach (var missing in missingDeps)
            {
                Console.WriteLine($"    - Missing: {missing}");
            }
        }
    }
}
```
### Mod Query & Summary Methods

#### `GetModById(string modId, string folderPath)`
Get metadata for a mod by its ID.

**Example:**
```
var jeiMeta = analyzer.GetModById("jei", @"C:\mods");

if (jeiMeta != null)
{
    Console.WriteLine($"Found JEI: {jeiMeta.name} v{jeiMeta.modVersion}");
    Console.WriteLine($"Description: {jeiMeta.description}");
}
else
{
    Console.WriteLine("JEI not found in mods folder");
}
```
#### `GetModVersion(string modPath)`
Get the version of a mod.

**Example:**
```
var version = analyzer.GetModVersion(@"C:\mods\jei.jar");
Console.WriteLine($"JEI version: {version ?? "Unknown"}");

// Batch check versions
var mods = Directory.GetFiles(@"C:\mods", "*.jar");
Console.WriteLine("Mod versions:");
foreach (var mod in mods)
{
    var ver = analyzer.GetModVersion(mod);
    var name = Path.GetFileNameWithoutExtension(mod);
    Console.WriteLine($"  {name}: {ver ?? "Unknown"}");
}
```
#### `GetModFileName(string modId, string folderPath)`
Get the file name of a mod by its ID.

**Example:**
```
var fileName = analyzer.GetModFileName("jei", @"C:\mods");
if (fileName != null)
{
    Console.WriteLine($"JEI file: {fileName}");
    // You can now use this to get the full path
    var fullPath = Path.Combine(@"C:\mods", fileName);
    Console.WriteLine($"Full path: {fullPath}");
}
```
#### `GetModSummary(string modPath)`
Get a comprehensive summary string of all key metadata.

**Example:**
```
var summary = analyzer.GetModSummary(@"C:\mods\jei.jar");
Console.WriteLine("=== JEI Summary ===");
Console.WriteLine(summary);
```
**Example output:**

=== JEI Summary ===
ID: jei
Name: Just Enough Items
Version: 15.2.0.27
Loader: Forge
LoaderVersion: 47.1.0
MCVersion: 1.20.1
Required: [forge, minecraft]
Optional: [waila, nei]
Incompatibilities: [toomanyitems]
#### `GetModsByLoader(string loader, string folderPath)`
Get all mods in a folder that use a specific loader.

**Example:**
```
var forgeMods = analyzer.GetModsByLoader("Forge", @"C:\mods");
var fabricMods = analyzer.GetModsByLoader("Fabric", @"C:\mods");

Console.WriteLine($"Forge mods ({forgeMods.Count}):");
foreach (var mod in forgeMods)
{
    Console.WriteLine($"  - {mod.name} ({mod.modId})");
}

Console.WriteLine($"\nFabric mods ({fabricMods.Count}):");
foreach (var mod in fabricMods)
{
    Console.WriteLine($"  - {mod.name} ({mod.modId})");
}

// Check for mixed loaders (potential issue)
if (forgeMods.Count > 0 && fabricMods.Count > 0)
{
    Console.WriteLine("\n⚠️ Warning: Mixed Forge and Fabric mods detected!");
}
```

#### `GetModsByMinecraftVersion(string mcVersion, string folderPath)`
Get all mods compatible with a specific Minecraft version.

**Example:**
```
var mods1201 = analyzer.GetModsByMinecraftVersion("1.20.1", @"C:\mods");
var mods1194 = analyzer.GetModsByMinecraftVersion("1.19.4", @"C:\mods");

Console.WriteLine($"Mods compatible with 1.20.1 ({mods1201.Count}):");
foreach (var mod in mods1201)
{
    Console.WriteLine($"  - {mod.name} ({mod.minecraftVersion})");
}

// Find mods that work with multiple versions
var modsWithWildcard = analyzer.GetModsByMinecraftVersion("1.20.x", @"C:\mods");
Console.WriteLine($"\nMods supporting 1.20.x ({modsWithWildcard.Count}):");
foreach (var mod in modsWithWildcard)
{
    Console.WriteLine($"  - {mod.name}");
}

```

### Crash Report Analysis

#### `ResolveCrashReport(string modsFolderPath, string crashReport)`
Analyze a crash report to identify potentially problematic mods.

**Example:**

```
// Read crash report from file
var crashReport = File.ReadAllText(@"C:\crash-reports\crash-2023-12-01.txt");

// Analyze the crash
var result = analyzer.ResolveCrashReport(@"C:\mods", crashReport);

if (result is List<string> suspiciousMods)
{
    Console.WriteLine($"🔍 Found {suspiciousMods.Count} potentially problematic mods:");
    foreach (var modId in suspiciousMods)
    {
        var modMeta = analyzer.GetModById(modId, @"C:\mods");
        if (modMeta != null)
        {
            Console.WriteLine($"  - {modMeta.name} ({modId}) v{modMeta.modVersion}");
        }
        else
        {
            Console.WriteLine($"  - {modId}");
        }
    }
    Console.WriteLine("\n💡 Try removing these mods one by one to isolate the issue.");
    Console.WriteLine("💡 Check for mod updates or compatibility issues.");
}
else
{
    Console.WriteLine("ℹ️ No obvious problematic mods identified in crash report.");
    Console.WriteLine("💡 The crash might be caused by:");
    Console.WriteLine("   - Missing dependencies");
    Console.WriteLine("   - Incompatible mod versions");
    Console.WriteLine("   - Java/Minecraft version issues");
}

// Advanced: Combine with other analysis
var missingDeps = analyzer.GetModsWithMissingDependencies(@"C:\mods");
if (missingDeps.Count > 0)
{
    Console.WriteLine($"\n⚠️ Also check missing dependencies for {missingDeps.Count} mods");
}
```
## Complete Example: Mod Folder Analysis

Here's a comprehensive example that uses multiple methods to analyze a mods folder:

```
using DrMod;

class Program
{
    static void Main()
    {
        var analyzer = new DrMod();
        var modsPath = @"C:\Minecraft\mods";
        
        Console.WriteLine("=== DrMod Analysis Report ===\n");
        
        // 1. Basic folder validation
        var errors = analyzer.ValidateModsFolder(modsPath);
        Console.WriteLine($"📋 Validation: {(errors.Count == 0 ? "✅ PASSED" : $"❌ {errors.Count} ISSUES")}");
        
        if (errors.Count > 0)
        {
            foreach (var error in errors.Take(5)) // Show first 5 errors
            {
                Console.WriteLine($"   {error}");
            }
            if (errors.Count > 5) Console.WriteLine($"   ... and {errors.Count - 5} more");
        }
        
        // 2. Mod inventory
        var allMods = analyzer.ReadAllModMetadataInFolder(modsPath);
        Console.WriteLine($"\n📦 Total mods: {allMods.Count}");
        
        var byLoader = allMods.GroupBy(m => m.loader).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (loader, count) in byLoader)
        {
            Console.WriteLine($"   {loader}: {count} mods");
        }
        
        // 3. Dependency analysis
        var missingDeps = analyzer.GetModsWithMissingDependencies(modsPath);
        Console.WriteLine($"\n🔗 Dependencies: {(missingDeps.Count == 0 ? "✅ ALL SATISFIED" : $"❌ {missingDeps.Count} MISSING")}");
        
        // 4. Conflict detection
        var conflicts = analyzer.DetectConflicts(Directory.GetFiles(modsPath, "*.jar").ToList());
        var incompatibilities = analyzer.DetectIncompatibilities(Directory.GetFiles(modsPath, "*.jar").ToList());
        
        Console.WriteLine($"\n⚔️ Conflicts: {conflicts.Count} duplicates, {incompatibilities.Count} incompatibilities");
        
        // 5. Most depended-on mods
        Console.WriteLine("\n🌟 Most popular dependencies:");
        var dependencyCounts = new Dictionary<string, int>();
        
        foreach (var mod in allMods)
        {
            foreach (var dep in mod.requiredDependencies)
            {
                dependencyCounts[dep] = dependencyCounts.GetValueOrDefault(dep, 0) + 1;
            }
        }
        
        var topDeps = dependencyCounts.OrderByDescending(kvp => kvp.Value).Take(5);
        foreach (var (dep, count) in topDeps)
        {
            Console.WriteLine($"   {dep}: required by {count} mods");
        }
        
        Console.WriteLine("\n✅ Analysis complete!");
    }
}

```
## License
MIT