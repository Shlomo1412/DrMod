using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DrMod
{
    public class DrMod
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
            ModMetadata? metadata = null;
            
            // Try NeoForge first
            var neoforgeEntry = archive.GetEntry("META-INF/neoforge.mods.toml");
            if (neoforgeEntry != null)
            {
                using var reader = new StreamReader(neoforgeEntry.Open());
                var lines = new List<string>();
                while (!reader.EndOfStream)
                    lines.Add(reader.ReadLine()!);
                metadata = ParseForgeModTomlLines(lines, "NeoForge");
            }
            // Try Forge
            else
            {
                var tomlEntry = archive.GetEntry("META-INF/mods.toml");
                if (tomlEntry != null)
                {
                    using var reader = new StreamReader(tomlEntry.Open());
                    var lines = new List<string>();
                    while (!reader.EndOfStream)
                        lines.Add(reader.ReadLine()!);
                    metadata = ParseForgeModTomlLines(lines, "Forge");
                }
            }
            
            // Try Quilt if no Forge/NeoForge found
            if (metadata == null)
            {
                var quiltEntry = archive.GetEntry("quilt.mod.json");
                if (quiltEntry != null)
                {
                    using var reader = new StreamReader(quiltEntry.Open());
                    var json = reader.ReadToEnd();
                    metadata = ParseFabricModJsonString(json, "Quilt");
                }
            }
            
            // Try Fabric if no other loader found
            if (metadata == null)
            {
                var fabricEntry = archive.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    using var reader = new StreamReader(fabricEntry.Open());
                    var json = reader.ReadToEnd();
                    metadata = ParseFabricModJsonString(json, "Fabric");
                }
            }
            
            // Set the filename if metadata was found
            if (metadata != null)
            {
                metadata.modFileName = Path.GetFileName(jarPath);
            }
            
            return metadata;
        }

        private ModMetadata? ReadForgeModToml(string filePath, string loader)
        {
            var lines = File.ReadAllLines(filePath);
            return ParseForgeModTomlLines(lines, loader);
        }

        // Enhanced: Parse required, optional dependencies, incompatibilities, mod version, and package
        private ModMetadata? ParseForgeModTomlLines(IEnumerable<string> lines, string loader)
        {
            var metadata = new ModMetadata();
            metadata.loader = loader;
            var currentSection = string.Empty;
            var isInModSection = false;
            var isInDependencySection = false;
            var currentDependencyModId = string.Empty;
            var isInMultilineString = false;
            var multilineStringBuilder = new StringBuilder();
            var multilineStringKey = string.Empty;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Handle multiline strings (triple quotes)
                if (isInMultilineString)
                {
                    if (trimmed.EndsWith("'''"))
                    {
                        // End of multiline string
                        multilineStringBuilder.Append(" " + trimmed.Substring(0, trimmed.Length - 3));
                        var multilineValue = multilineStringBuilder.ToString().Trim();
                        
                        // Apply the multiline value to the appropriate property
                        if (isInModSection && multilineStringKey == "description")
                            metadata.description = multilineValue;
                        
                        isInMultilineString = false;
                        multilineStringBuilder.Clear();
                        multilineStringKey = string.Empty;
                    }
                    else
                    {
                        // Continue collecting multiline content
                        multilineStringBuilder.Append(" " + trimmed);
                    }
                    continue;
                }
                
                // Skip empty lines and comments
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;
                
                // Track section changes
                if (trimmed.StartsWith("["))
                {
                    currentSection = trimmed;
                    isInModSection = trimmed.StartsWith("[[mods]]");
                    isInDependencySection = trimmed.StartsWith("[[dependencies.");
                    
                    if (isInDependencySection)
                    {
                        // Reset dependency context for new dependency
                        currentDependencyModId = string.Empty;
                    }
                }
                
                // Parse mod information (only in [[mods]] section)
                if (isInModSection)
                {
                    if (line.Contains("modId"))
                        metadata.modId = GetTomlValue(line);
                    else if (line.Contains("displayName"))
                        metadata.name = GetTomlValue(line);
                    else if (line.Contains("description"))
                    {
                        var descValue = GetTomlValue(line);
                        if (descValue != null && descValue.StartsWith("'''"))
                        {
                            // Start of multiline string
                            isInMultilineString = true;
                            multilineStringKey = "description";
                            multilineStringBuilder.Append(descValue.Substring(3));
                        }
                        else
                        {
                            metadata.description = descValue;
                        }
                    }
                    else if (line.Contains("version"))
                        metadata.modVersion = GetTomlValue(line);
                }
                
                // Parse global properties (can be anywhere in file when not in specific sections)
                if (!isInModSection && !isInDependencySection)
                {
                    if (line.Contains("loaderVersion"))
                        metadata.loaderVersion = GetTomlValue(line);
                    else if (line.Contains("mcVersion") || line.Contains("minecraftVersion"))
                        metadata.minecraftVersion = GetTomlValue(line);
                }
                
                // Parse dependencies (in [[dependencies.*]] sections)
                if (isInDependencySection)
                {
                    if (line.Contains("modId"))
                    {
                        currentDependencyModId = GetTomlValue(line) ?? "";
                    }
                    else if (line.Contains("mandatory") && !string.IsNullOrEmpty(currentDependencyModId))
                    {
                        var mandatoryValue = GetTomlValue(line);
                        if (mandatoryValue?.ToLower() == "true")
                        {
                            if (!metadata.requiredDependencies.Contains(currentDependencyModId) && currentDependencyModId != metadata.modId)
                                metadata.requiredDependencies.Add(currentDependencyModId);
                        }
                        else if (mandatoryValue?.ToLower() == "false")
                        {
                            if (!metadata.optionalDependencies.Contains(currentDependencyModId) && currentDependencyModId != metadata.modId)
                                metadata.optionalDependencies.Add(currentDependencyModId);
                        }
                    }
                    else if (line.Contains("versionRange") && currentDependencyModId == "minecraft")
                    {
                        // Extract Minecraft version from versionRange
                        var versionRange = GetTomlValue(line);
                        if (!string.IsNullOrEmpty(versionRange))
                        {
                            metadata.minecraftVersion = ExtractMinecraftVersionFromRange(versionRange);
                        }
                    }
                }
                
                // Handle incompatibilities (Forge/NeoForge: [[incompatibilities.MODID]])
                if (currentSection.StartsWith("[[incompatibilities."))
                {
                    var incId = currentSection.Replace("[[incompatibilities.", "").Replace("]]", "").Trim();
                    if (!string.IsNullOrEmpty(incId) && !metadata.incompatibilities.Contains(incId))
                        metadata.incompatibilities.Add(incId);
                }
            }
            
            return metadata;
        }

        /// <summary>
        /// Extracts a Minecraft version from a version range string like "[1.20.1,)" or "[1.20.1,1.21.0)"
        /// </summary>
        private string ExtractMinecraftVersionFromRange(string versionRange)
        {
            if (string.IsNullOrEmpty(versionRange))
                return "";

            // Remove brackets and parentheses
            var cleaned = versionRange.Trim('[', ']', '(', ')');
            
            // Split by comma and take the first part (minimum version)
            var parts = cleaned.Split(',');
            if (parts.Length > 0)
            {
                var minVersion = parts[0].Trim();
                return string.IsNullOrEmpty(minVersion) ? "" : minVersion;
            }

            return "";
        }

        private string? GetTomlValue(string line)
        {
            // Handle different TOML value formats
            if (!line.Contains('='))
                return null;

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                return null;

            var value = parts[1].Trim();

            // Remove quotes (single or double) if present
            if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                (value.StartsWith("'") && value.EndsWith("'")))
            {
                if (value.Length >= 2)
                {
                    value = value.Substring(1, value.Length - 2);
                    // Unescape common TOML escape sequences
                    value = value.Replace("\\\"", "\"")
                                 .Replace("\\'", "'")
                                 .Replace("\\\\", "\\")
                                 .Replace("\\n", "\n")
                                 .Replace("\\r", "\r")
                                 .Replace("\\t", "\t");
                }
            }

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private ModMetadata? ReadFabricModJson(string filePath, string loader)
        {
            var json = File.ReadAllText(filePath);
            return ParseFabricModJsonString(json, loader);
        }

        // Enhanced: Parse required, optional dependencies, incompatibilities, mod version, and package for Fabric/Quilt
        private ModMetadata? ParseFabricModJsonString(string json, string loader)
        {
            try
            {
                // Sanitize JSON by fixing common issues with unescaped characters
                json = SanitizeJsonString(json);
                
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var metadata = new ModMetadata();
                metadata.loader = loader;
                
                if (root.TryGetProperty("id", out var idProp))
                    metadata.modId = idProp.GetString();
                if (root.TryGetProperty("name", out var nameProp))
                    metadata.name = nameProp.GetString();
                if (root.TryGetProperty("description", out var descProp))
                    metadata.description = descProp.GetString();
                if (root.TryGetProperty("version", out var verProp))
                    metadata.modVersion = verProp.GetString();
                
                // Parse dependencies
                if (root.TryGetProperty("depends", out var dependsProp))
                {
                    if (dependsProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var dep in dependsProp.EnumerateObject())
                        {
                            if (dep.Name == "minecraft")
                            {
                                // Handle minecraft version specially
                                if (dep.Value.ValueKind == JsonValueKind.String)
                                {
                                    metadata.minecraftVersion = dep.Value.GetString();
                                }
                                else if (dep.Value.ValueKind == JsonValueKind.Array)
                                {
                                    // If minecraft is an array, take the first element
                                    var firstElement = dep.Value.EnumerateArray().FirstOrDefault();
                                    if (firstElement.ValueKind == JsonValueKind.String)
                                    {
                                        metadata.minecraftVersion = firstElement.GetString();
                                    }
                                }
                            }
                            else
                            {
                                // Add to required dependencies if not minecraft
                                metadata.requiredDependencies.Add(dep.Name);
                            }
                        }
                    }
                    else if (dependsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dep in dependsProp.EnumerateArray())
                        {
                            if (dep.ValueKind == JsonValueKind.String)
                            {
                                var depId = dep.GetString();
                                if (depId != null && depId != "minecraft")
                                    metadata.requiredDependencies.Add(depId);
                            }
                            else if (dep.ValueKind == JsonValueKind.Object && dep.TryGetProperty("id", out var idDepProp))
                            {
                                var depId = idDepProp.GetString();
                                if (depId != null && depId != "minecraft")
                                    metadata.requiredDependencies.Add(depId);
                            }
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
                
                return metadata;
            }
            catch (Exception ex)
            {
                // Return null if parsing fails, but don't crash the entire application
                Console.WriteLine($"Error parsing Fabric/Quilt mod JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sanitizes JSON string by fixing common issues with unescaped characters that cause parsing errors.
        /// </summary>
        private string SanitizeJsonString(string json)
        {
            if (string.IsNullOrEmpty(json))
                return json;

            try
            {
                // Fix unescaped characters within JSON string values
                // This regex finds string values and replaces problematic characters within them
                var stringValuePattern = @"""([^""\\]*(\\.[^""\\]*)*)""";
                
                json = Regex.Replace(json, stringValuePattern, match =>
                {
                    var fullMatch = match.Value;
                    var stringContent = match.Groups[1].Value;
                    
                    // Only process if this looks like it contains unescaped characters
                    if (stringContent.Contains('\n') || stringContent.Contains('\r') || stringContent.Contains('\t'))
                    {
                        // Escape common problematic characters
                        stringContent = stringContent
                            .Replace("\\", "\\\\")  // Escape backslashes first
                            .Replace("\n", "\\n")   // Escape newlines (0x0A)
                            .Replace("\r", "\\r")   // Escape carriage returns (0x0D)
                            .Replace("\t", "\\t")   // Escape tabs
                            .Replace("\"", "\\\""); // Escape quotes
                        
                        return $"\"{stringContent}\"";
                    }
                    
                    return fullMatch;
                });

                return json;
            }
            catch
            {
                // If sanitization fails, return the original string
                return json;
            }
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

        // 1. GetModById: Retrieve ModMetadata for a specific mod ID from a folder
        public ModMetadata? GetModById(string modId, string folderPath)
        {
            var mods = ReadAllModMetadataInFolder(folderPath);
            return mods.FirstOrDefault(m => string.Equals(m.modId, modId, StringComparison.OrdinalIgnoreCase));
        }

        // 2. GetAllDependencies: Recursively resolve all dependencies for a mod
        public HashSet<string> GetAllDependencies(string modPath, bool includeOptional = false)
        {
            var allDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Visit(string path)
            {
                if (!visited.Add(path)) return;
                var meta = ReadModMetadata(path);
                if (meta == null || string.IsNullOrEmpty(meta.modId)) return;
                var folder = Path.GetDirectoryName(path)!;
                foreach (var dep in meta.requiredDependencies)
                {
                    if (allDeps.Add(dep))
                    {
                        var depMeta = GetModById(dep, folder);
                        if (depMeta != null && !string.IsNullOrEmpty(depMeta.modFileName))
                            Visit(Path.Combine(folder, depMeta.modFileName));
                    }
                }
                if (includeOptional)
                {
                    foreach (var dep in meta.optionalDependencies)
                    {
                        if (allDeps.Add(dep))
                        {
                            var depMeta = GetModById(dep, folder);
                            if (depMeta != null && !string.IsNullOrEmpty(depMeta.modFileName))
                                Visit(Path.Combine(folder, depMeta.modFileName));
                        }
                    }
                }
            }
            Visit(modPath);
            return allDeps;
        }

        // 3. GetDependents: Return mods that depend on a given mod
        public List<string> GetDependents(string modId, string folderPath)
        {
            var mods = ReadAllModMetadataInFolder(folderPath);
            return mods.Where(m => m.requiredDependencies.Contains(modId, StringComparer.OrdinalIgnoreCase) || m.optionalDependencies.Contains(modId, StringComparer.OrdinalIgnoreCase))
                       .Select(m => m.modId ?? "")
                       .Where(id => !string.IsNullOrEmpty(id))
                       .ToList();
        }

        // 4. GetModVersion: Return the version of a mod
        public string? GetModVersion(string modPath)
        {
            var meta = ReadModMetadata(modPath);
            return meta?.modVersion;
        }

        // 5. GetModFileName: Return the file name of a mod by its mod ID
        public string? GetModFileName(string modId, string folderPath)
        {
            var mods = ReadAllModMetadataInFolder(folderPath);
            var meta = mods.FirstOrDefault(m => string.Equals(m.modId, modId, StringComparison.OrdinalIgnoreCase));
            return meta?.modFileName;
        }

        // 6. GetModSummary: Return a summary string with all key metadata
        public string GetModSummary(string modPath)
        {
            var meta = ReadModMetadata(modPath);
            if (meta == null) return "Mod not found or invalid.";
            return $"ID: {meta.modId}\nName: {meta.name}\nVersion: {meta.modVersion}\nLoader: {meta.loader}\nLoaderVersion: {meta.loaderVersion}\nMCVersion: {meta.minecraftVersion}\nRequired: [{string.Join(", ", meta.requiredDependencies)}]\nOptional: [{string.Join(", ", meta.optionalDependencies)}]\nIncompatibilities: [{string.Join(", ", meta.incompatibilities)}]";
        }

        // 7. GetModsByLoader: Return all mods in a folder that use a specific loader
        public List<ModMetadata> GetModsByLoader(string loader, string folderPath)
        {
            var mods = ReadAllModMetadataInFolder(folderPath);
            return mods.Where(m => string.Equals(m.loader, loader, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // 8. GetModsByMinecraftVersion: Return all mods compatible with a specific MC version
        public List<ModMetadata> GetModsByMinecraftVersion(string mcVersion, string folderPath)
        {
            var mods = ReadAllModMetadataInFolder(folderPath);
            return mods.Where(m => !string.IsNullOrEmpty(m.minecraftVersion) && IsVersionCompatible(m.minecraftVersion, mcVersion)).ToList();
        }

        // 9. GetModsWithMissingDependencies: Return mods with missing required dependencies
        public List<string> GetModsWithMissingDependencies(string folderPath)
        {
            var mods = ReadAllModMetadataInFolder(folderPath);
            var modIds = new HashSet<string>(mods.Select(m => m.modId ?? ""), StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var mod in mods)
            {
                foreach (var dep in mod.requiredDependencies)
                {
                    if (!modIds.Contains(dep))
                    {
                        if (!string.IsNullOrEmpty(mod.modId) && !result.Contains(mod.modId))
                            result.Add(mod.modId);
                    }
                }
            }
            return result;
        }



        /// <summary>
        /// Returns the required Java version (e.g., 8, 17, 21) for a given mod, based on its Minecraft version.
        /// </summary>
        public int GetRequiredJavaVersion(string modPath)
        {
            var metadata = ReadModMetadata(modPath);
            if (metadata == null || string.IsNullOrEmpty(metadata.minecraftVersion))
                return 17; // Default to Java 17 if unknown

            var parts = metadata.minecraftVersion.Split('.');
            if (parts.Length < 2) return 17;
            if (!int.TryParse(parts[1], out int minor)) return 17;

            // Version mapping:
            return minor >= 21 ? 21 :
                   minor >= 17 ? 17 : 8;
        }

        /// <summary>
        /// Finds the path to a suitable JDK for the required Java version for the given mod.
        /// Returns null if not found.
        /// </summary>
        public string? FindRequiredJavaVersion(string modPath)
        {
            int requiredJava = GetRequiredJavaVersion(modPath);

            // 1) Look under %LocalAppData%\Modrix\JDKs\jdk-<requiredJava>*
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var jdkRoot = Path.Combine(localAppData, "Modrix", "JDKs");
            if (Directory.Exists(jdkRoot))
            {
                var candidates = Directory
                    .GetDirectories(jdkRoot, $"jdk-{requiredJava}*")
                    .Where(IsValidJdk)
                    .OrderByDescending(d => d)
                    .ToList();
                if (candidates.Count > 0)
                    return candidates[0];
            }

            // 2) Check user's .jdks folder
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userJdksPath = Path.Combine(userProfile, ".jdks");
            if (Directory.Exists(userJdksPath))
            {
                var candidates = Directory
                    .GetDirectories(userJdksPath, $"*jdk-{requiredJava}*")
                    .Where(IsValidJdk)
                    .OrderByDescending(d => d)
                    .ToList();
                if (candidates.Count > 0)
                    return candidates[0];
            }

            // 3) JAVA_HOME
            var sysJavaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(sysJavaHome) && Directory.Exists(sysJavaHome) && IsValidJdk(sysJavaHome))
            {
                var version = GetJavaVersion(sysJavaHome);
                if (!string.IsNullOrEmpty(version) && version.StartsWith(requiredJava.ToString()))
                    return sysJavaHome;
            }

            // 4) Common installation paths
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var commonJdkPaths = new[]
            {
                Path.Combine(programFiles, "Java"),
                Path.Combine(programFiles, "Eclipse Foundation"),
                Path.Combine(programFiles, "AdoptOpenJDK"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java")
            };
            foreach (var basePath in commonJdkPaths)
            {
                if (Directory.Exists(basePath))
                {
                    var candidates = Directory
                        .GetDirectories(basePath, $"*jdk*{requiredJava}*")
                        .Where(IsValidJdk)
                        .OrderByDescending(d => d)
                        .ToList();
                    if (candidates.Count > 0)
                        return candidates[0];
                }
            }
            return null;
        }

        private bool IsValidJdk(string path)
        {
            var javaExe = Path.Combine(path, "bin", "java.exe");
            return File.Exists(javaExe);
        }

        private string GetJavaVersion(string jdkPath)
        {
            var javaExe = Path.Combine(jdkPath, "bin", "java.exe");
            try
            {
                var startInfo = new ProcessStartInfo(javaExe, "-version")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit(2000);
                    var versionOutput = process.StandardError.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(versionOutput))
                        versionOutput = process.StandardOutput.ReadToEnd();
                    var match = Regex.Match(
                        versionOutput,
                        @"version\s+""?(\d+(\.\d+)*)([._]\d+)?",
                        RegexOptions.IgnoreCase
                    );
                    return match.Success ? match.Groups[1].Value : "Unknown";
                }
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Get the file size of a mod in bytes.
        /// </summary>
        public long GetModFileSize(string modPath)
        {
            if (!File.Exists(modPath))
                return 0;
            
            try
            {
                return new FileInfo(modPath).Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get all mods in a folder sorted by estimated memory usage.
        /// </summary>
        public List<ModMetadata> GetModsByMemoryUsage(string folderPath, bool sortDescending = true)
        {
            var mods = ReadAllModMetadataInFolder(folderPath);
            var modsWithSize = new List<(ModMetadata mod, long size)>();

            foreach (var mod in mods)
            {
                var modFiles = Directory.GetFiles(folderPath, "*.jar")
                    .Where(f => {
                        var meta = ReadModMetadata(f);
                        return meta?.modId?.Equals(mod.modId, StringComparison.OrdinalIgnoreCase) == true;
                    });

                long totalSize = 0;
                foreach (var file in modFiles)
                {
                    totalSize += GetModFileSize(file);
                }
                modsWithSize.Add((mod, totalSize));
            }

            var sorted = sortDescending 
                ? modsWithSize.OrderByDescending(x => x.size)
                : modsWithSize.OrderBy(x => x.size);

            return sorted.Select(x => x.mod).ToList();
        }

        /// <summary>
        /// Detect mods that may have performance impact based on file size and known patterns.
        /// </summary>
        public List<string> DetectPerformanceImpactingMods(string folderPath)
        {
            var performanceImpactingMods = new List<string>();
            var mods = ReadAllModMetadataInFolder(folderPath);

            // Known performance-heavy mod patterns
            var heavyModPatterns = new[]
            {
                "optifine", "shaders", "iris", "sodium", "lithium", "phosphor",
                "create", "thermal", "mekanism", "industrialcraft", "buildcraft",
                "galacticraft", "forestry", "gregtech", "techreborn", "appliedenergistics",
                "refined", "storage", "mystical", "botania", "thaumcraft", "blood",
                "astral", "dimensional", "rftools", "enderio", "actuallyadditions"
            };

            foreach (var mod in mods)
            {
                if (string.IsNullOrEmpty(mod.modId)) continue;

                // Check for known heavy mods
                foreach (var pattern in heavyModPatterns)
                {
                    if (mod.modId.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                        mod.name?.Contains(pattern, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        performanceImpactingMods.Add(mod.modId);
                        break;
                    }
                }

                // Check file size (mods > 10MB are potentially heavy)
                var modFiles = Directory.GetFiles(folderPath, "*.jar")
                    .Where(f => {
                        var meta = ReadModMetadata(f);
                        return meta?.modId?.Equals(mod.modId, StringComparison.OrdinalIgnoreCase) == true;
                    });

                foreach (var file in modFiles)
                {
                    if (GetModFileSize(file) > 10 * 1024 * 1024) // 10MB
                    {
                        if (!performanceImpactingMods.Contains(mod.modId))
                            performanceImpactingMods.Add(mod.modId);
                        break;
                    }
                }
            }

            return performanceImpactingMods;
        }

        /// <summary>
        /// Check if a mod is safe to remove (no other mods depend on it).
        /// </summary>
        public bool IsModSafeToRemove(string modId, string folderPath)
        {
            var dependents = GetDependents(modId, folderPath);
            return dependents.Count == 0;
        }

        /// <summary>
        /// Get list of required mods for a world/save based on level.dat analysis.
        /// </summary>
        public List<string> GetWorldRequiredMods(string worldPath)
        {
            var requiredMods = new List<string>();
            
            try
            {
                // Look for level.dat file
                var levelDatPath = Path.Combine(worldPath, "level.dat");
                if (!File.Exists(levelDatPath))
                    return requiredMods;

                // Simple heuristic: scan for mod IDs in various world files
                var worldFiles = new[]
                {
                    Path.Combine(worldPath, "level.dat"),
                    Path.Combine(worldPath, "data", "capabilities.dat"),
                    Path.Combine(worldPath, "data", "villages.dat")
                };

                var commonModIds = new[]
                {
                    "minecraft", "forge", "neoforge", "fabric", "quilt",
                    "jei", "waila", "hwyla", "theoneprobe", "journeymap",
                    "optifine", "create", "thermal", "mekanism", "botania"
                };

                foreach (var file in worldFiles)
                {
                    if (!File.Exists(file)) continue;
                    
                    try
                    {
                        var content = File.ReadAllText(file);
                        foreach (var modId in commonModIds)
                        {
                            if (content.Contains(modId, StringComparison.OrdinalIgnoreCase) && 
                                !requiredMods.Contains(modId))
                            {
                                requiredMods.Add(modId);
                            }
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read as text
                    }
                }

                // Check for mod-specific world data folders
                var dataPath = Path.Combine(worldPath, "data");
                if (Directory.Exists(dataPath))
                {
                    var datFiles = Directory.GetFiles(dataPath, "*.dat");
                    foreach (var datFile in datFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(datFile);
                        if (!commonModIds.Contains(fileName) && !requiredMods.Contains(fileName))
                        {
                            requiredMods.Add(fileName);
                        }
                    }
                }
            }
            catch
            {
                // Return empty list if world can't be analyzed
            }

            return requiredMods;
        }

        /// <summary>
        /// Check compatibility between a world and current mod setup.
        /// </summary>
        public WorldCompatibilityReport CheckWorldCompatibility(string worldPath, string modsFolder)
        {
            var report = new WorldCompatibilityReport();
            
            try
            {
                var worldRequiredMods = GetWorldRequiredMods(worldPath);
                var installedMods = ReadAllModMetadataInFolder(modsFolder);
                var installedModIds = installedMods.Select(m => m.modId ?? "").Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                report.RequiredMods = worldRequiredMods;
                report.MissingMods = worldRequiredMods.Where(mod => !installedModIds.Contains(mod)).ToList();
                
                // Check for version compatibility
                foreach (var installedMod in installedMods)
                {
                    if (string.IsNullOrEmpty(installedMod.modId)) continue;
                    
                    // Simple heuristic: if MC versions don't match, it's potentially incompatible
                    if (!string.IsNullOrEmpty(installedMod.minecraftVersion))
                    {
                        report.MinecraftVersion = installedMod.minecraftVersion;
                        report.ModLoader = installedMod.loader ?? "";
                        break;
                    }
                }

                report.IsCompatible = report.MissingMods.Count == 0;
                
                if (report.MissingMods.Count > 0)
                {
                    report.Warnings.Add($"Missing {report.MissingMods.Count} required mods");
                }
            }
            catch (Exception ex)
            {
                report.IsCompatible = false;
                report.Warnings.Add($"Error analyzing world: {ex.Message}");
            }

            return report;
        }

        /// <summary>
        /// Find corrupted mod files that can't be read or have invalid metadata.
        /// </summary>
        public List<string> FindCorruptedMods(string folderPath)
        {
            var corruptedMods = new List<string>();
            var modFiles = Directory.GetFiles(folderPath, "*.jar");

            foreach (var modFile in modFiles)
            {
                try
                {
                    // Try to read the mod
                    var metadata = ReadModMetadata(modFile);
                    
                    // Check if we can open the ZIP file
                    using var archive = ZipFile.OpenRead(modFile);
                    
                    // Basic validation - if we can't read metadata, it might be corrupted
                    if (metadata == null)
                    {
                        corruptedMods.Add(modFile);
                        continue;
                    }

                    // Check for essential metadata
                    if (string.IsNullOrEmpty(metadata.modId) && string.IsNullOrEmpty(metadata.name))
                    {
                        corruptedMods.Add(modFile);
                    }
                }
                catch
                {
                    // If any exception occurs, consider the mod corrupted
                    corruptedMods.Add(modFile);
                }
            }

            return corruptedMods;
        }

        /// <summary>
        /// Attempt to repair a corrupted mod by re-downloading or validation.
        /// </summary>
        public bool RepairMod(string modPath)
        {
            try
            {
                // Basic repair: try to validate the file
                if (!File.Exists(modPath))
                    return false;

                // Try to read the mod to see if it's actually corrupted
                var metadata = ReadModMetadata(modPath);
                
                // If we can read metadata, the mod might not be corrupted
                if (metadata != null && !string.IsNullOrEmpty(metadata.modId))
                    return true;

                // For now, we can't actually repair mods without external sources
                // This would require integration with mod repositories
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Import a modpack from .mrpack (Modrinth) or .zip (CurseForge) file.
        /// Returns detailed import information including extracted metadata.
        /// </summary>
        public ModPackImportResult ImportModPack(string modPackPath, string destinationPath)
        {
            var result = new ModPackImportResult
            {
                Success = false,
                ModPackPath = modPackPath,
                DestinationPath = destinationPath
            };

            try
            {
                if (!File.Exists(modPackPath))
                {
                    result.ErrorMessage = "Modpack file does not exist.";
                    return result;
                }

                if (!Directory.Exists(destinationPath))
                    Directory.CreateDirectory(destinationPath);

                using var archive = ZipFile.OpenRead(modPackPath);

                if (modPackPath.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                {
                    return ImportModrinthPack(archive, destinationPath, result);
                }
                else if (modPackPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return ImportCurseForgePack(archive, destinationPath, result);
                }

                result.ErrorMessage = "Unsupported modpack format. Only .mrpack and .zip files are supported.";
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error importing modpack: {ex.Message}";
                return result;
            }
        }

        private ModPackImportResult ImportModrinthPack(ZipArchive archive, string destinationPath, ModPackImportResult result)
        {
            try
            {
                // Look for modrinth.index.json
                var indexEntry = archive.GetEntry("modrinth.index.json");
                if (indexEntry == null)
                {
                    result.ErrorMessage = "Invalid .mrpack file: modrinth.index.json not found.";
                    return result;
                }

                using var reader = new StreamReader(indexEntry.Open());
                var indexJson = reader.ReadToEnd();
                using var doc = JsonDocument.Parse(indexJson);
                var root = doc.RootElement;

                // Extract modpack metadata
                var modPackInfo = new ModPackInfo();
                if (root.TryGetProperty("name", out var nameProp))
                    modPackInfo.Name = nameProp.GetString() ?? "";
                if (root.TryGetProperty("versionId", out var versionProp))
                    modPackInfo.Version = versionProp.GetString() ?? "";
                if (root.TryGetProperty("summary", out var summaryProp))
                    modPackInfo.Summary = summaryProp.GetString();

                // Extract dependencies (Minecraft version, mod loader, etc.)
                if (root.TryGetProperty("dependencies", out var depsProp))
                {
                    if (depsProp.TryGetProperty("minecraft", out var mcProp))
                        modPackInfo.MinecraftVersion = mcProp.GetString() ?? "";
                    if (depsProp.TryGetProperty("forge", out var forgeProp))
                        modPackInfo.ModLoader = "Forge";
                    else if (depsProp.TryGetProperty("neoforge", out var neoForgeProp))
                        modPackInfo.ModLoader = "NeoForge";
                    else if (depsProp.TryGetProperty("fabric-loader", out var fabricProp))
                        modPackInfo.ModLoader = "Fabric";
                    else if (depsProp.TryGetProperty("quilt-loader", out var quiltProp))
                        modPackInfo.ModLoader = "Quilt";
                }

                result.ModPackInfo = modPackInfo;

                // Extract files from the "files" array
                var extractedFiles = new List<string>();
                var skippedFiles = new List<string>();

                if (root.TryGetProperty("files", out var filesArray))
                {
                    foreach (var file in filesArray.EnumerateArray())
                    {
                        try
                        {
                            var fileResult = ProcessModrinthFile(file, archive, destinationPath);
                            if (fileResult.Success)
                                extractedFiles.AddRange(fileResult.ExtractedFiles);
                            else
                                skippedFiles.Add(fileResult.ErrorMessage ?? "Unknown error");
                        }
                        catch (Exception ex)
                        {
                            skippedFiles.Add($"Error processing file: {ex.Message}");
                        }
                    }
                }

                // Extract overrides folders (in order: overrides, server-overrides, client-overrides)
                ExtractOverrideFolder(archive, "overrides", destinationPath, extractedFiles);
                ExtractOverrideFolder(archive, "server-overrides", destinationPath, extractedFiles);
                ExtractOverrideFolder(archive, "client-overrides", destinationPath, extractedFiles);

                result.Success = true;
                result.ExtractedFiles = extractedFiles;
                result.SkippedFiles = skippedFiles;
                result.Summary = $"Successfully imported {extractedFiles.Count} files from {modPackInfo.Name}";

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error parsing Modrinth modpack: {ex.Message}";
                return result;
            }
        }

        private (bool Success, List<string> ExtractedFiles, string? ErrorMessage) ProcessModrinthFile(JsonElement file, ZipArchive archive, string destinationPath)
        {
            var extractedFiles = new List<string>();

            if (!file.TryGetProperty("path", out var pathProp))
                return (false, extractedFiles, "File missing 'path' property");

            var filePath = pathProp.GetString();
            if (string.IsNullOrEmpty(filePath))
                return (false, extractedFiles, "File path is empty");

            // Security: Prevent path traversal attacks
            if (filePath.Contains("..") || filePath.StartsWith("/") || filePath.StartsWith("\\") || 
                (filePath.Length >= 3 && filePath[1] == ':' && (filePath[0] >= 'A' && filePath[0] <= 'Z')))
            {
                return (false, extractedFiles, $"Security: Invalid file path '{filePath}' (path traversal attempt)");
            }

            // Check environment requirements (client/server compatibility)
            if (file.TryGetProperty("env", out var envProp))
            {
                // For now, we'll extract all files, but this could be configurable
                // based on whether we're setting up a client or server
                if (envProp.TryGetProperty("client", out var clientProp))
                {
                    var clientReq = clientProp.GetString();
                    if (clientReq == "unsupported")
                    {
                        // This is a server-only file, might want to skip for client installs
                        // For now, we'll still extract it
                    }
                }
            }

            // Try to find the file in the archive first
            var entry = archive.GetEntry(filePath);
            if (entry != null)
            {
                var fileName = Path.GetFileName(filePath);
                var destFile = Path.Combine(destinationPath, fileName);
                entry.ExtractToFile(destFile, true);
                extractedFiles.Add(destFile);
                return (true, extractedFiles, null);
            }

            // If file is not in archive, it needs to be downloaded
            if (file.TryGetProperty("downloads", out var downloadsProp) && downloadsProp.ValueKind == JsonValueKind.Array)
            {
                var downloads = downloadsProp.EnumerateArray().ToList();
                if (downloads.Count > 0)
                {
                    // For now, we'll note that the file needs to be downloaded but won't actually download it
                    // This would require HTTP client implementation and is beyond the current scope
                    return (false, extractedFiles, $"File '{filePath}' requires download from external URL (not yet implemented)");
                }
            }

            return (false, extractedFiles, $"File '{filePath}' not found in archive and no download URLs provided");
        }

        private void ExtractOverrideFolder(ZipArchive archive, string overrideFolderName, string destinationPath, List<string> extractedFiles)
        {
            try
            {
                var overrideEntries = archive.Entries
                    .Where(e => e.FullName.StartsWith($"{overrideFolderName}/", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var entry in overrideEntries)
                {
                    if (entry.FullName.EndsWith("/")) // Skip directories
                        continue;

                    // Remove the override folder prefix from the path
                    var relativePath = entry.FullName.Substring(overrideFolderName.Length + 1);
                    
                    // Security: Prevent path traversal
                    if (relativePath.Contains(".."))
                        continue;

                    var destFile = Path.Combine(destinationPath, relativePath);
                    var destDir = Path.GetDirectoryName(destFile);
                    
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    entry.ExtractToFile(destFile, true);
                    extractedFiles.Add(destFile);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail the entire import
                Console.WriteLine($"Warning: Error extracting {overrideFolderName} folder: {ex.Message}");
            }
        }

        private ModPackImportResult ImportCurseForgePack(ZipArchive archive, string destinationPath, ModPackImportResult result)
        {
            try
            {
                // Look for manifest.json
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                {
                    result.ErrorMessage = "Invalid CurseForge modpack: manifest.json not found.";
                    return result;
                }

                using var reader = new StreamReader(manifestEntry.Open());
                var manifestJson = reader.ReadToEnd();
                using var doc = JsonDocument.Parse(manifestJson);
                var root = doc.RootElement;

                // Extract CurseForge metadata
                var modPackInfo = new ModPackInfo();
                if (root.TryGetProperty("name", out var nameProp))
                    modPackInfo.Name = nameProp.GetString() ?? "";
                if (root.TryGetProperty("version", out var versionProp))
                    modPackInfo.Version = versionProp.GetString() ?? "";
                if (root.TryGetProperty("author", out var authorProp))
                    modPackInfo.Author = authorProp.GetString();

                // Extract Minecraft info
                if (root.TryGetProperty("minecraft", out var mcProp))
                {
                    if (mcProp.TryGetProperty("version", out var mcVerProp))
                        modPackInfo.MinecraftVersion = mcVerProp.GetString() ?? "";
                    
                    if (mcProp.TryGetProperty("modLoaders", out var loadersProp) && loadersProp.ValueKind == JsonValueKind.Array)
                    {
                        var firstLoader = loadersProp.EnumerateArray().FirstOrDefault();
                        if (firstLoader.TryGetProperty("id", out var loaderIdProp))
                        {
                            var loaderId = loaderIdProp.GetString() ?? "";
                            if (loaderId.StartsWith("forge", StringComparison.OrdinalIgnoreCase))
                                modPackInfo.ModLoader = "Forge";
                            else if (loaderId.StartsWith("neoforge", StringComparison.OrdinalIgnoreCase))
                                modPackInfo.ModLoader = "NeoForge";
                            else if (loaderId.StartsWith("fabric", StringComparison.OrdinalIgnoreCase))
                                modPackInfo.ModLoader = "Fabric";
                            else if (loaderId.StartsWith("quilt", StringComparison.OrdinalIgnoreCase))
                                modPackInfo.ModLoader = "Quilt";
                        }
                    }
                }

                result.ModPackInfo = modPackInfo;

                // Extract overrides folder (contains mods and configs)
                var extractedFiles = new List<string>();
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                    {
                        if (entry.FullName.EndsWith("/")) // Skip directories
                            continue;

                        var relativePath = entry.FullName.Substring("overrides/".Length);
                        
                        // Security: Prevent path traversal
                        if (relativePath.Contains(".."))
                            continue;

                        var destFile = Path.Combine(destinationPath, relativePath);
                        var destDir = Path.GetDirectoryName(destFile);
                        
                        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                            Directory.CreateDirectory(destDir);

                        entry.ExtractToFile(destFile, true);
                        extractedFiles.Add(destFile);
                    }
                }

                result.Success = true;
                result.ExtractedFiles = extractedFiles;
                result.Summary = $"Successfully imported {extractedFiles.Count} files from {modPackInfo.Name}";

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error parsing CurseForge modpack: {ex.Message}";
                return result;
            }
        }
    }
}
