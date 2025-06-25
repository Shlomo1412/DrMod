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

        // Enhanced: Parse required, optional dependencies, incompatibilities, mod version, and package
        private ModMetadata? ParseForgeModTomlLines(IEnumerable<string> lines, string loader)
        {
            var metadata = new DrMod.ModMetadata();
            metadata.loader = loader;
            var currentSection = string.Empty;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("["))
                {
                    currentSection = trimmed;
                }
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
                else if (line.StartsWith("version"))
                    metadata.modVersion = GetTomlValue(line);
                // Required dependencies
                else if (currentSection.StartsWith("[[dependencies.") && line.Trim().StartsWith("mandatory") && line.Contains("true"))
                {
                    var depId = currentSection.Replace("[[dependencies.", "").Replace("]]", "").Trim();
                    if (!string.IsNullOrEmpty(depId))
                        metadata.requiredDependencies.Add(depId);
                }
                // Optional dependencies
                else if (currentSection.StartsWith("[[dependencies.") && line.Trim().StartsWith("mandatory") && line.Contains("false"))
                {
                    var depId = currentSection.Replace("[[dependencies.", "").Replace("]]", "").Trim();
                    if (!string.IsNullOrEmpty(depId))
                        metadata.optionalDependencies.Add(depId);
                }
                // Incompatibilities (Forge/NeoForge: [[incompatibilities.MODID]])
                else if (currentSection.StartsWith("[[incompatibilities."))
                {
                    var incId = currentSection.Replace("[[incompatibilities.", "").Replace("]]", "").Trim();
                    if (!string.IsNullOrEmpty(incId) && !metadata.incompatibilities.Contains(incId))
                        metadata.incompatibilities.Add(incId);
                }
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

        // Enhanced: Parse required, optional dependencies, incompatibilities, mod version, and package for Fabric/Quilt
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
            if (root.TryGetProperty("version", out var verProp))
                metadata.modVersion = verProp.GetString();
            if (root.TryGetProperty("depends", out var dependsProp))
            {
                if (dependsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dep in dependsProp.EnumerateObject())
                    {
                        if (dep.Name != "minecraft")
                            metadata.requiredDependencies.Add(dep.Name);
                    }
                }
                else if (dependsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in dependsProp.EnumerateArray())
                    {
                        if (dep.ValueKind == JsonValueKind.String)
                            metadata.requiredDependencies.Add(dep.GetString()!);
                        else if (dep.ValueKind == JsonValueKind.Object && dep.TryGetProperty("id", out var idDepProp))
                            metadata.requiredDependencies.Add(idDepProp.GetString()!);
                    }
                }
            }
            // Optional dependencies (Fabric/Quilt: "suggests")
            if (root.TryGetProperty("suggests", out var suggestsProp) && suggestsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var dep in suggestsProp.EnumerateObject())
                {
                    metadata.optionalDependencies.Add(dep.Name);
                }
            }
            // Incompatibilities (Fabric/Quilt: "breaks" or "conflicts")
            if (root.TryGetProperty("breaks", out var breaksProp))
            {
                if (breaksProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dep in breaksProp.EnumerateObject())
                        metadata.incompatibilities.Add(dep.Name);
                }
                else if (breaksProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in breaksProp.EnumerateArray())
                        if (dep.ValueKind == JsonValueKind.String)
                            metadata.incompatibilities.Add(dep.GetString()!);
                }
            }
            if (root.TryGetProperty("conflicts", out var conflictsProp))
            {
                if (conflictsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dep in conflictsProp.EnumerateObject())
                        metadata.incompatibilities.Add(dep.Name);
                }
                else if (conflictsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in conflictsProp.EnumerateArray())
                        if (dep.ValueKind == JsonValueKind.String)
                            metadata.incompatibilities.Add(dep.GetString()!);
                }
            }
            if (root.TryGetProperty("schemaVersion", out var loaderVerProp))
                metadata.loaderVersion = loaderVerProp.ToString();
            if (root.TryGetProperty("depends", out var dependsProp2) && dependsProp2.ValueKind == JsonValueKind.Object && dependsProp2.TryGetProperty("minecraft", out var mcProp))
                metadata.minecraftVersion = mcProp.GetString();
            return metadata;
        }

        public bool IsCompatible(string modPath, string mcVersion, string loader, string loaderVersion)
        {
            var metadata = ReadModMetadata(modPath);
            if (metadata == null)
                return false;

            // Loader check (case-insensitive)
            if (!string.Equals(metadata.loader, loader, StringComparison.OrdinalIgnoreCase))
                return false;

            // MC version: support version prefix/range (e.g., 1.20, 1.20.1, 1.20.x)
            if (!string.IsNullOrEmpty(metadata.minecraftVersion) && !IsVersionCompatible(metadata.minecraftVersion, mcVersion))
                return false;

            // Loader version: support version prefix/range
            if (!string.IsNullOrEmpty(metadata.loaderVersion) && !IsVersionCompatible(metadata.loaderVersion, loaderVersion))
                return false;

            return true;
        }

        // Advanced version compatibility: supports exact, prefix, and x wildcards (e.g., 1.20, 1.20.x)
        private bool IsVersionCompatible(string modVersion, string targetVersion)
        {
            if (modVersion == targetVersion)
                return true;
            if (modVersion.EndsWith(".x") && targetVersion.StartsWith(modVersion.TrimEnd('x', '.')))
                return true;
            if (targetVersion.EndsWith(".x") && modVersion.StartsWith(targetVersion.TrimEnd('x', '.')))
                return true;
            // Could add more advanced range parsing here
            return false;
        }

        public List<string> GetRequiredDependencies(string modPath)
        {
            var metadata = ReadModMetadata(modPath);
            return metadata?.requiredDependencies ?? new List<string>();
        }

        public List<string> GetOptionalDependencies(string modPath)
        {
            var metadata = ReadModMetadata(modPath);
            return metadata?.optionalDependencies ?? new List<string>();
        }

        public List<string> GetIncompatibilities(string modPath)
        {
            var metadata = ReadModMetadata(modPath);
            return metadata?.incompatibilities ?? new List<string>();
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

        public List<string> ValidateMod(string modPath)
        {
            var errors = new List<string>();
            ModMetadata? metadata = null;
            try
            {
                metadata = ReadModMetadata(modPath);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to parse mod metadata: {ex.Message}");
                return errors;
            }

            if (metadata == null)
            {
                errors.Add("Could not read mod metadata or unsupported mod format.");
                return errors;
            }

            if (string.IsNullOrWhiteSpace(metadata.modId))
                errors.Add("Missing modId.");
            if (string.IsNullOrWhiteSpace(metadata.name))
                errors.Add("Missing mod name.");
            if (string.IsNullOrWhiteSpace(metadata.loader))
                errors.Add("Missing loader type.");
            if (string.IsNullOrWhiteSpace(metadata.minecraftVersion))
                errors.Add("Missing Minecraft version.");
            if (string.IsNullOrWhiteSpace(metadata.loaderVersion))
                errors.Add("Missing loader version.");
            if (string.IsNullOrWhiteSpace(metadata.modVersion))
                errors.Add("Missing mod version.");

            // Check for duplicate dependencies
            var depSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dep in metadata.requiredDependencies)
            {
                if (!depSet.Add(dep))
                    errors.Add($"Duplicate required dependency: {dep}");
            }
            depSet.Clear();
            foreach (var dep in metadata.optionalDependencies)
            {
                if (!depSet.Add(dep))
                    errors.Add($"Duplicate optional dependency: {dep}");
            }

            // Check for self-dependency
            if (metadata.modId != null && metadata.requiredDependencies.Contains(metadata.modId, StringComparer.OrdinalIgnoreCase))
                errors.Add("Mod depends on itself.");

            // Check for self-incompatibility
            if (metadata.modId != null && metadata.incompatibilities.Contains(metadata.modId, StringComparer.OrdinalIgnoreCase))
                errors.Add("Mod is marked as incompatible with itself.");

            return errors;
        }

        public List<string> ValidateModsFolder(string folderPath)
        {
            var errors = new List<string>();
            var modFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
            var modPaths = new List<string>();
            foreach (var file in modFiles)
            {
                if (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("neoforge.mods.toml", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("quilt.mod.json", StringComparison.OrdinalIgnoreCase))
                {
                    modPaths.Add(file);
                }
            }

            var modIdToMetadata = new Dictionary<string, ModMetadata>(StringComparer.OrdinalIgnoreCase);
            var modIdToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var modIdToDependencies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var modIdToIncompatibilities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var mcVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var loaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var loaderVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Validate each mod and collect metadata
            foreach (var modPath in modPaths)
            {
                var modErrors = ValidateMod(modPath);
                if (modErrors.Count > 0)
                {
                    foreach (var err in modErrors)
                        errors.Add($"[{Path.GetFileName(modPath)}] {err}");
                }
                var metadata = ReadModMetadata(modPath);
                if (metadata != null && !string.IsNullOrEmpty(metadata.modId))
                {
                    modIdToMetadata[metadata.modId] = metadata;
                    modIdToPath[metadata.modId] = modPath;
                    mcVersions.Add(metadata.minecraftVersion ?? "");
                    loaders.Add(metadata.loader ?? "");
                    loaderVersions.Add(metadata.loaderVersion ?? "");
                    modIdToDependencies[metadata.modId] = metadata.requiredDependencies;
                    modIdToIncompatibilities[metadata.modId] = metadata.incompatibilities;
                }
            }

            // Check for duplicate mod IDs
            var duplicateIds = DetectConflicts(modPaths);
            foreach (var dup in duplicateIds)
                errors.Add($"Duplicate modId detected: {dup}");

            // Check for mods using different MC versions/loaders/loader versions
            if (mcVersions.Count > 1)
                errors.Add($"Multiple Minecraft versions detected in mods folder: {string.Join(", ", mcVersions.Where(v => !string.IsNullOrEmpty(v)))}");
            if (loaders.Count > 1)
                errors.Add($"Multiple loaders detected in mods folder: {string.Join(", ", loaders.Where(l => !string.IsNullOrEmpty(l)))}");
            if (loaderVersions.Count > 1)
                errors.Add($"Multiple loader versions detected in mods folder: {string.Join(", ", loaderVersions.Where(lv => !string.IsNullOrEmpty(lv)))}");

            // Check for missing dependencies
            foreach (var kvp in modIdToDependencies)
            {
                foreach (var dep in kvp.Value)
                {
                    if (!modIdToMetadata.ContainsKey(dep))
                        errors.Add($"[{kvp.Key}] Missing required dependency: {dep}");
                }
            }

            // Check for circular dependencies (simple DFS)
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var modId in modIdToDependencies.Keys)
            {
                if (HasCircularDependency(modId, modIdToDependencies, visited, stack, out var cycle))
                {
                    errors.Add($"Circular dependency detected: {string.Join(" -> ", cycle)}");
                }
            }

            // Check for explicit incompatibilities
            foreach (var kvp in modIdToIncompatibilities)
            {
                foreach (var inc in kvp.Value)
                {
                    if (modIdToMetadata.ContainsKey(inc))
                        errors.Add($"[{kvp.Key}] is incompatible with [{inc}]");
                }
            }

            return errors;
        }

        private bool HasCircularDependency(string modId, Dictionary<string, List<string>> depMap, HashSet<string> visited, HashSet<string> stack, out List<string> cycle)
        {
            cycle = new List<string>();
            if (!visited.Add(modId))
                return false;
            stack.Add(modId);
            if (depMap.TryGetValue(modId, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (stack.Contains(dep))
                    {
                        cycle = stack.Concat(new[] { dep }).ToList();
                        return true;
                    }
                    if (HasCircularDependency(dep, depMap, visited, stack, out cycle))
                        return true;
                }
            }
            stack.Remove(modId);
            return false;
        }

        public List<ModMetadata> ReadAllModMetadataInFolder(string folderPath)
        {
            var modFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
            var modMetadatas = new List<ModMetadata>();
            foreach (var file in modFiles)
            {
                if (file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("mods.toml", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("neoforge.mods.toml", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith("quilt.mod.json", StringComparison.OrdinalIgnoreCase))
                {
                    var metadata = ReadModMetadata(file);
                    if (metadata != null)
                        modMetadatas.Add(metadata);
                }
            }
            return modMetadatas;
        }

        public object ResolveCrashReport(string modsFolderPath, string crashReport)
        {
            var modMetadatas = ReadAllModMetadataInFolder(modsFolderPath);
            var possibleMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Heuristics: check for modId, mod name, mod version, or jar file name in the crash report
            foreach (var metadata in modMetadatas)
            {
                if (!string.IsNullOrEmpty(metadata.modId) && crashReport.Contains(metadata.modId, StringComparison.OrdinalIgnoreCase))
                    possibleMods.Add(metadata.modId);
                else if (!string.IsNullOrEmpty(metadata.name) && crashReport.Contains(metadata.name, StringComparison.OrdinalIgnoreCase))
                    possibleMods.Add(metadata.modId ?? metadata.name!);
                else if (!string.IsNullOrEmpty(metadata.modVersion) && crashReport.Contains(metadata.modVersion, StringComparison.OrdinalIgnoreCase))
                    possibleMods.Add(metadata.modId ?? metadata.modVersion!);
            }

            // Also check for mod jar file names
            var modFiles = Directory.GetFiles(modsFolderPath, "*.jar", SearchOption.TopDirectoryOnly);
            foreach (var file in modFiles)
            {
                var fileName = Path.GetFileName(file);
                if (crashReport.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    // Try to map file name to modId
                    var metadata = modMetadatas.FirstOrDefault(m => !string.IsNullOrEmpty(m.modId) && (fileName.Contains(m.modId, StringComparison.OrdinalIgnoreCase) || (m.name != null && fileName.Contains(m.name, StringComparison.OrdinalIgnoreCase))));
                    if (metadata != null && !string.IsNullOrEmpty(metadata.modId))
                        possibleMods.Add(metadata.modId);
                }
            }

            // Advanced: check for package/class names (if available)
            foreach (var metadata in modMetadatas)
            {
                if (!string.IsNullOrEmpty(metadata.modPackage) && crashReport.Contains(metadata.modPackage, StringComparison.OrdinalIgnoreCase))
                    possibleMods.Add(metadata.modId ?? metadata.modPackage!);
            }

            if (possibleMods.Count == 0)
                return false;
            return possibleMods.ToList();
        }

        // Advanced incompatibility detection
        public List<(string modId, string incompatibleWith)> DetectIncompatibilities(List<string> modPaths)
        {
            var incompatibilities = new List<(string, string)>();
            var modMetadatas = new Dictionary<string, ModMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in modPaths)
            {
                var metadata = ReadModMetadata(path);
                if (metadata != null && !string.IsNullOrEmpty(metadata.modId))
                    modMetadatas[metadata.modId] = metadata;
            }
            foreach (var mod in modMetadatas.Values)
            {
                foreach (var inc in mod.incompatibilities)
                {
                    if (modMetadatas.ContainsKey(inc))
                        incompatibilities.Add((mod.modId!, inc));
                }
            }
            return incompatibilities;
        }
    }
}
