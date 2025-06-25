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
}
