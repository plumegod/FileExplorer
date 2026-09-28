using FileExplorer.Core;

namespace FileExplorer.Tools
{
    public sealed class ToolPresetDefinition
    {
        internal ToolPresetDefinition(
            string id,
            string groupName,
            string displayNameResourceKey,
            string executableRole,
            string expectedFileName,
            ToolPresetAction action,
            ItemTypeFilter itemTypeFilter,
            SelectionFilter selectionFilter,
            string extensionFilter,
            bool confirmBeforeRun,
            string commandSummary)
        {
            Id = id;
            GroupName = groupName;
            DisplayNameResourceKey = displayNameResourceKey;
            ExecutableRole = executableRole;
            ExpectedFileName = expectedFileName;
            Action = action;
            ItemTypeFilter = itemTypeFilter;
            SelectionFilter = selectionFilter;
            ExtensionFilter = extensionFilter;
            ConfirmBeforeRun = confirmBeforeRun;
            CommandSummary = commandSummary;
        }

        public string Id { get; }

        public string GroupName { get; }

        public string DisplayNameResourceKey { get; }

        public string ExecutableRole { get; }

        public string ExpectedFileName { get; }

        public ItemTypeFilter ItemTypeFilter { get; }

        public SelectionFilter SelectionFilter { get; }

        public string ExtensionFilter { get; }

        public bool ConfirmBeforeRun { get; }

        public string CommandSummary { get; }

        internal ToolPresetAction Action { get; }
    }

    internal enum ToolPresetAction
    {
        GitBash,
        GitGui,
        TortoiseGitCommit,
        TortoiseGitSync,
        TortoiseGitPull,
        TortoiseGitPush,
        TortoiseGitLog,
        TortoiseGitDiff,
        TortoiseSvnUpdate,
        TortoiseSvnCommit,
        TortoiseSvnLog,
        TortoiseSvnDiff,
        TortoiseSvnCleanup,
        TortoiseSvnRepositoryBrowser,
        SevenZipOpen,
        SevenZipExtractHere,
        SevenZipExtractFolder,
        SevenZipCompress
    }

    public static class ToolExecutableRole
    {
        public const string GitBash = "git-bash";
        public const string GitGui = "git-gui";
        public const string TortoiseGit = "tortoisegit-proc";
        public const string TortoiseSvn = "tortoisesvn-proc";
        public const string SevenZipFileManager = "7zip-file-manager";
        public const string SevenZipGui = "7zip-gui";
    }
}
