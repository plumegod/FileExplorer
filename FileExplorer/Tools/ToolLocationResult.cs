namespace FileExplorer.Tools
{
    public sealed class ToolLocationResult
    {
        private ToolLocationResult(bool isFound, string executablePath, string source, bool hasInvalidOverride)
        {
            IsFound = isFound;
            ExecutablePath = executablePath;
            Source = source;
            HasInvalidOverride = hasInvalidOverride;
        }

        public bool IsFound { get; }
        public string ExecutablePath { get; }
        public string Source { get; }
        public bool HasInvalidOverride { get; }

        public static ToolLocationResult Found(string path, string source)
        {
            return new ToolLocationResult(true, path, source, false);
        }

        public static ToolLocationResult Missing(bool invalidOverride = false, string path = null)
        {
            return new ToolLocationResult(false, path, invalidOverride ? "invalid-override" : "missing", invalidOverride);
        }
    }

    public sealed class ToolLocationCandidate
    {
        public ToolLocationCandidate(string executablePath, string source, ToolLocationStage stage = ToolLocationStage.Registered)
        {
            ExecutablePath = executablePath;
            Source = source;
            Stage = stage;
        }

        public string ExecutablePath { get; }
        public string Source { get; }
        public ToolLocationStage Stage { get; }
    }

    public enum ToolLocationStage
    {
        Registered,
        CommonDirectory,
        Path
    }

    public interface IToolLocator
    {
        ToolLocationResult Locate(string executableRole);
        ToolLocationResult SaveOverride(string executableRole, string executablePath);
        void ClearOverride(string executableRole);
    }
}
