using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;

namespace DrMod
{
    public class Class1
    {
        // Reads mod metadata from a given file path (.jar, mods.toml, neoforge.mods.toml, fabric.mod.json, or quilt.mod.json)
        public ModMetadata? ReadModMetadata(string filePath)
        {
            if (filePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                return ReadModMetadataFromJar(filePath);
            }
            else if (filePath.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase))
            {
                return ReadForgeModToml(filePath, "Forge");
            }
            else if (filePath.EndsWith("neoforge.mods.toml", StringComparison.OrdinalIgnoreCase))
            {
                return ReadForgeModToml(filePath, "NeoForge");
            }
            else if (filePath.EndsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase))
            {
                return ReadFabricModJson(filePath, "Fabric");
            }
            else if (filePath.EndsWith("quilt.mod.json", StringComparison.OrdinalIgnoreCase))
            {
                return ReadFabricModJson(filePath, "Quilt");
            }
            return null;
        }

        private ModMetadata? ReadModMetadataFromJar(string jarPath)
        {
            using var archive = ZipFile.OpenRead(jarPath);
            // Try NeoForge first
            var neoforgeEntry = archive.GetEntry("META-INF/neoforge.mods.toml");
            if (neoforgeEntry != null)
            {
                using var reader = new StreamReader(neoforgeEntry.Open());
                var lines = new List<string>();
                while (!reader.EndOfStream)
                    lines.Add(reader.ReadLine()!);
                return ParseForgeModTomlLines(lines, "NeoForge");
            }
            // Try Forge
            var tomlEntry = archive.GetEntry("META-INF/mods.toml");
            if (tomlEntry != null)
            {
                using var reader = new StreamReader(tomlEntry.Open());
                var lines = new List<string>();
                while (!reader.EndOfStream)
                    lines.Add(reader.ReadLine()!);
                return ParseForgeModTomlLines(lines, "Forge");
            }
            // Try Quilt
            var quiltEntry = archive.GetEntry("quilt.mod.json");
            if (quiltEntry != null)
            {
                using var reader = new StreamReader(quiltEntry.Open());
                var json = reader.ReadToEnd();
                return ParseFabricModJsonString(json, "Quilt");
            }
            // Try Fabric
            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry != null)
            {
                using var reader = new StreamReader(fabricEntry.Open());
                var json = reader.ReadToEnd();
                return ParseFabricModJsonString(json, "Fabric");
            }
            return null;
        }

        private ModMetadata? ReadForgeModToml(string filePath, string loader)
        {
            var lines = File.ReadAllLines(filePath);
            return ParseForgeModTomlLines(lines, loader);
        }

        private ModMetadata? ParseForgeModTomlLines(IEnumerable<string> lines, string loader)
        {
            var metadata = new DrMod.ModMetadata();
            metadata.loader = loader;
            foreach (var line in lines)
            {
                if (line.StartsWith("modId"))
                    metadata.modId = GetTomlValue(line);
                else if (line.StartsWith("displayName"))
                    metadata.name = GetTomlValue(line);
                else if (line.StartsWith("description"))
                    metadata.description = GetTomlValue(line);
                else if (line.StartsWith("loaderVersion"))
                    metadata.loaderVersion = GetTomlValue(line);
                else if (line.StartsWith("mcVersion"))
                    metadata.minecraftVersion = GetTomlValue(line);
            }
            return metadata;
        }

        private string? GetTomlValue(string line)
        {
            var match = Regex.Match(line, "=\\s*['\"]?(.*?)['\"]?$");
            return match.Success ? match.Groups[1].Value : null;
        }

        private ModMetadata? ReadFabricModJson(string filePath, string loader)
        {
            var json = File.ReadAllText(filePath);
            return ParseFabricModJsonString(json, loader);
        }

        private ModMetadata? ParseFabricModJsonString(string json, string loader)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var metadata = new DrMod.ModMetadata();
            metadata.loader = loader;
            if (root.TryGetProperty("id", out var idProp))
                metadata.modId = idProp.GetString();
            if (root.TryGetProperty("name", out var nameProp))
                metadata.name = nameProp.GetString();
            if (root.TryGetProperty("description", out var descProp))
                metadata.description = descProp.GetString();
            if (root.TryGetProperty("depends", out var dependsProp) && dependsProp.TryGetProperty("minecraft", out var mcProp))
                metadata.minecraftVersion = mcProp.GetString();
            if (root.TryGetProperty("schemaVersion", out var loaderVerProp))
                metadata.loaderVersion = loaderVerProp.ToString();
            return metadata;
        }

        public bool IsCompatible(string modPath, string mcVersion, string loader, string loaderVersion)
        {
            var metadata = ReadModMetadata(modPath);
            if (metadata == null)
                return false;

            // Compare loader (case-insensitive)
            if (!string.Equals(metadata.loader, loader, StringComparison.OrdinalIgnoreCase))
                return false;

            // Compare MC version (exact match for now)
            if (!string.IsNullOrEmpty(metadata.minecraftVersion) && metadata.minecraftVersion != mcVersion)
                return false;

            // Compare loader version (exact match for now)
            if (!string.IsNullOrEmpty(metadata.loaderVersion) && metadata.loaderVersion != loaderVersion)
                return false;

            return true;
        }

        public List<string> GetRequiredDependencies(string modPath)
        {
            var dependencies = new List<string>();
            if (modPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(modPath);
                // NeoForge/Forge
                var neoforgeEntry = archive.GetEntry("META-INF/neoforge.mods.toml");
                if (neoforgeEntry != null)
                {
                    using var reader = new StreamReader(neoforgeEntry.Open());
                    dependencies.AddRange(ParseForgeTomlDependencies(reader.ReadToEnd()));
                    return dependencies;
                }
                var forgeEntry = archive.GetEntry("META-INF/mods.toml");
                if (forgeEntry != null)
                {
                    using var reader = new StreamReader(forgeEntry.Open());
                    dependencies.AddRange(ParseForgeTomlDependencies(reader.ReadToEnd()));
                    return dependencies;
                }
                // Quilt
                var quiltEntry = archive.GetEntry("quilt.mod.json");
                if (quiltEntry != null)
                {
                    using var reader = new StreamReader(quiltEntry.Open());
                    dependencies.AddRange(ParseFabricJsonDependencies(reader.ReadToEnd()));
                    return dependencies;
                }
                // Fabric
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using var reader = new StreamReader(fabricEntry.Open());
                    dependencies.AddRange(ParseFabricJsonDependencies(reader.ReadToEnd()));
                    return dependencies;
                }
            }
            else if (modPath.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase) || modPath.EndsWith("neoforge.mods.toml", StringComparison.OrdinalIgnoreCase))
            {
                dependencies.AddRange(ParseForgeTomlDependencies(File.ReadAllText(modPath)));
            }
            else if (modPath.EndsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase) || modPath.EndsWith("quilt.mod.json", StringComparison.OrdinalIgnoreCase))
            {
                dependencies.AddRange(ParseFabricJsonDependencies(File.ReadAllText(modPath)));
            }
            return dependencies;
        }

        private List<string> ParseForgeTomlDependencies(string toml)
        {
            var dependencies = new List<string>();
            // Simple regex to find required dependencies: [[dependencies.MODID]] id = "..." mandatory = true
            var depBlocks = Regex.Matches(toml, @"\[\[dependencies\.(.*?)\]\](.*?)mandatory\s*=\s*true", RegexOptions.Singleline);
            foreach (Match block in depBlocks)
            {
                // The group 1 is the modid of the dependency
                if (block.Groups.Count > 1)
                    dependencies.Add(block.Groups[1].Value);
            }
            return dependencies;
        }

        private List<string> ParseFabricJsonDependencies(string json)
        {
            var dependencies = new List<string>();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("depends", out var dependsProp))
            {
                if (dependsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dep in dependsProp.EnumerateObject())
                    {
                        if (dep.Name != "minecraft") // skip minecraft itself
                            dependencies.Add(dep.Name);
                    }
                }
                else if (dependsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in dependsProp.EnumerateArray())
                    {
                        if (dep.ValueKind == JsonValueKind.String)
                            dependencies.Add(dep.GetString()!);
                        else if (dep.ValueKind == JsonValueKind.Object && dep.TryGetProperty("id", out var idProp))
                            dependencies.Add(idProp.GetString()!);
                    }
                }
            }
            return dependencies;
        }

        public List<string> DetectConflicts(List<string> modPaths)
        {
            var modIdToPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in modPaths)
            {
                var metadata = ReadModMetadata(path);
                if (metadata == null || string.IsNullOrEmpty(metadata.modId))
                    continue;
                if (!modIdToPaths.ContainsKey(metadata.modId!))
                    modIdToPaths[metadata.modId!] = new List<string>();
                modIdToPaths[metadata.modId!].Add(path);
            }

            // Detect duplicate mod IDs
            foreach (var kvp in modIdToPaths)
            {
                if (kvp.Value.Count > 1)
                    conflicts.Add(kvp.Key);
            }

            // (Future: Add dependency and explicit incompatibility checks here)

            return conflicts.ToList();
        }
    }
}
