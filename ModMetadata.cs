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

    public class ModPackImportResult
    {
        public bool Success { get; set; }
        public string ModPackPath { get; set; } = "";
        public string DestinationPath { get; set; } = "";
        public ModPackInfo? ModPackInfo { get; set; }
        public List<string> ExtractedFiles { get; set; } = new();
        public List<string> SkippedFiles { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? Summary { get; set; }
    }

    // Progress reporting interfaces and classes
    public interface IProgress<in T>
    {
        void Report(T value);
    }

    public class Progress<T> : IProgress<T>
    {
        private readonly Action<T>? _handler;

        public Progress() { }

        public Progress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value)
        {
            _handler?.Invoke(value);
        }
    }

    public class ProgressInfo
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string Status { get; set; } = "";
        public string? CurrentItem { get; set; }
        public double PercentComplete => Total > 0 ? (double)Current / Total * 100 : 0;
        public string? SubStatus { get; set; }
        public OperationType Operation { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Elapsed => DateTime.Now - StartTime;
        public TimeSpan? EstimatedTimeRemaining => PercentComplete > 0 && PercentComplete < 100 
            ? TimeSpan.FromMilliseconds(Elapsed.TotalMilliseconds * (100 - PercentComplete) / PercentComplete) 
            : null;
    }

    public enum OperationType
    {
        ReadingMods,
        ValidatingMods,
        ImportingModPack,
        AnalyzingPerformance,
        DetectingConflicts,
        CheckingCorruption,
        AnalyzingDependencies,
        WorldCompatibilityCheck,
        FindingJavaVersions,
        RepairingMods
    }

    public class DetailedProgressInfo : ProgressInfo
    {
        public List<string> CompletedItems { get; set; } = new();
        public List<string> FailedItems { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Dictionary<string, object> AdditionalData { get; set; } = new();
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public double BytesPerSecond { get; set; }
    }

    public class CancellationToken
    {
        private volatile bool _isCancellationRequested;

        public bool IsCancellationRequested => _isCancellationRequested;

        public void Cancel()
        {
            _isCancellationRequested = true;
        }

        public void ThrowIfCancellationRequested()
        {
            if (_isCancellationRequested)
            {
                throw new OperationCanceledException("Operation was cancelled.");
            }
        }
    }

    public class OperationCanceledException : Exception
    {
        public OperationCanceledException() : base("Operation was cancelled.") { }
        public OperationCanceledException(string message) : base(message) { }
        public OperationCanceledException(string message, Exception innerException) : base(message, innerException) { }
    }
}
