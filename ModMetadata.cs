namespace DrMod
{
    public class ModMetadata
    {
        public string? modId;
        public string? name;
        public string? description;
        public string? minecraftVersion;
        public string? loader;
        public string? loaderVersion;
        public List<string> requiredDependencies = new();
        public List<string> optionalDependencies = new();
        public List<string> incompatibilities = new();
        public string? modFileName;
        public string? modPackage;
        public string? modVersion;
    }

    public class ModPerformanceInfo
    {
        public string ModPath { get; set; } = "";
        public string? ModId { get; set; }
        public string? ModName { get; set; }
        public long FileSizeBytes { get; set; }
        public int EstimatedMemoryUsageMB { get; set; }
        public string? PerformanceCategory { get; set; } // "Light", "Medium", "Heavy", "Unknown"
        public List<string> PerformanceWarnings { get; set; } = new();
    }

    public class WorldCompatibilityReport
    {
        public bool IsCompatible { get; set; }
        public List<string> RequiredMods { get; set; } = new();
        public List<string> MissingMods { get; set; } = new();
        public List<string> IncompatibleMods { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string MinecraftVersion { get; set; } = "";
        public string ModLoader { get; set; } = "";
    }

    public class ModPackInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string MinecraftVersion { get; set; } = "";
        public string ModLoader { get; set; } = "";
        public List<ModPackFile> Files { get; set; } = new();
        public string? Summary { get; set; }
        public string? Author { get; set; }
    }

    public class ModPackFile
    {
        public string Path { get; set; } = "";
        public string? Url { get; set; }
        public string? Sha1 { get; set; }
        public long Size { get; set; }
        public bool Required { get; set; } = true;
    }
}
