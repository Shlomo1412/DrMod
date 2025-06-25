using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.IO.Compression;

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
    }
}
