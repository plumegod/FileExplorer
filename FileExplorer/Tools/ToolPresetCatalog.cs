using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FileExplorer.Core;
using FileExplorer.Model;

namespace FileExplorer.Tools
{
    public sealed class ToolPresetCatalog
    {
        public static ToolPresetCatalog Default { get; } = new ToolPresetCatalog();

        public IReadOnlyList<ToolPresetDefinition> Definitions { get; }

        private ToolPresetCatalog()
        {
            Definitions = new ReadOnlyCollection<ToolPresetDefinition>(CreateDefinitions());
        }

        public ToolPresetDefinition GetDefinition(string presetId)
        {
            ToolPresetDefinition definition = Definitions.FirstOrDefault(x => String.Equals(x.Id, presetId, StringComparison.Ordinal));
            if (definition == null)
                throw new ToolPresetException(ToolPresetError.UnknownPreset);
            return definition;
        }

        public string GetExpectedFileName(string executableRole)
        {
            string[] fileNames = Definitions
                .Where(x => String.Equals(x.ExecutableRole, executableRole, StringComparison.Ordinal))
                .Select(x => x.ExpectedFileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (fileNames.Length != 1)
                throw new ToolPresetException(ToolPresetError.UnknownPreset);
            return fileNames[0];
        }

        public ToolInvocation BuildInvocation(string presetId, string executablePath, IEnumerable<FileModel> selectedItems)
        {
            ToolPresetDefinition definition = GetDefinition(presetId);
            ValidateExecutable(definition, executablePath);

            List<FileModel> items = selectedItems?.Where(x => x != null).ToList() ?? new List<FileModel>();
            ValidateSelection(definition, items);

            FileModel first = items[0];
            string workingDirectory = definition.Action == ToolPresetAction.SevenZipCompress
                ? first.ParentPath
                : GetWorkingDirectory(first);
            if (String.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
                throw new ToolPresetException(ToolPresetError.WorkingDirectoryMissing);
            List<string> arguments = BuildArguments(definition.Action, items);
            return new ToolInvocation(executablePath, workingDirectory, arguments, definition.ConfirmBeforeRun);
        }

        private static List<ToolPresetDefinition> CreateDefinitions()
        {
            const string archives = ".7z|.zip|.rar|.tar|.gz|.bz2|.xz";
            return new List<ToolPresetDefinition>
            {
                Definition("git.bash-here", "Git", "ToolPresetGitBashHere", ToolExecutableRole.GitBash, "git-bash.exe", ToolPresetAction.GitBash, ItemTypeFilter.Folder, SelectionFilter.Single, null, false, "git-bash.exe --cd={folder}"),
                Definition("git.gui-here", "Git", "ToolPresetGitGuiHere", ToolExecutableRole.GitGui, "git-gui.exe", ToolPresetAction.GitGui, ItemTypeFilter.Folder, SelectionFilter.Single, null, false, "git-gui.exe --working-dir {folder}"),
                Definition("tgit.commit", "TortoiseGit", "ToolPresetCommit", ToolExecutableRole.TortoiseGit, "TortoiseGitProc.exe", ToolPresetAction.TortoiseGitCommit, ItemTypeFilter.None, SelectionFilter.Single, null, true, "/command:commit /path:{path}"),
                Definition("tgit.sync", "TortoiseGit", "ToolPresetSync", ToolExecutableRole.TortoiseGit, "TortoiseGitProc.exe", ToolPresetAction.TortoiseGitSync, ItemTypeFilter.Folder, SelectionFilter.Single, null, true, "/command:sync /path:{folder}"),
                Definition("tgit.pull", "TortoiseGit", "ToolPresetPull", ToolExecutableRole.TortoiseGit, "TortoiseGitProc.exe", ToolPresetAction.TortoiseGitPull, ItemTypeFilter.Folder, SelectionFilter.Single, null, true, "/command:pull /path:{folder}"),
                Definition("tgit.push", "TortoiseGit", "ToolPresetPush", ToolExecutableRole.TortoiseGit, "TortoiseGitProc.exe", ToolPresetAction.TortoiseGitPush, ItemTypeFilter.Folder, SelectionFilter.Single, null, true, "/command:push /path:{folder}"),
                Definition("tgit.log", "TortoiseGit", "ToolPresetLog", ToolExecutableRole.TortoiseGit, "TortoiseGitProc.exe", ToolPresetAction.TortoiseGitLog, ItemTypeFilter.None, SelectionFilter.Single, null, false, "/command:log /path:{path}"),
                Definition("tgit.diff", "TortoiseGit", "ToolPresetDiff", ToolExecutableRole.TortoiseGit, "TortoiseGitProc.exe", ToolPresetAction.TortoiseGitDiff, ItemTypeFilter.File, SelectionFilter.Single, null, false, "/command:diff /path:{path}"),
                Definition("tsvn.update", "TortoiseSVN", "ToolPresetUpdate", ToolExecutableRole.TortoiseSvn, "TortoiseProc.exe", ToolPresetAction.TortoiseSvnUpdate, ItemTypeFilter.None, SelectionFilter.Single, null, true, "/command:update /path:{path}"),
                Definition("tsvn.commit", "TortoiseSVN", "ToolPresetCommit", ToolExecutableRole.TortoiseSvn, "TortoiseProc.exe", ToolPresetAction.TortoiseSvnCommit, ItemTypeFilter.None, SelectionFilter.Single, null, true, "/command:commit /path:{path}"),
                Definition("tsvn.log", "TortoiseSVN", "ToolPresetLog", ToolExecutableRole.TortoiseSvn, "TortoiseProc.exe", ToolPresetAction.TortoiseSvnLog, ItemTypeFilter.None, SelectionFilter.Single, null, false, "/command:log /path:{path}"),
                Definition("tsvn.diff", "TortoiseSVN", "ToolPresetDiff", ToolExecutableRole.TortoiseSvn, "TortoiseProc.exe", ToolPresetAction.TortoiseSvnDiff, ItemTypeFilter.File, SelectionFilter.Single, null, false, "/command:diff /path:{path}"),
                Definition("tsvn.cleanup", "TortoiseSVN", "ToolPresetCleanup", ToolExecutableRole.TortoiseSvn, "TortoiseProc.exe", ToolPresetAction.TortoiseSvnCleanup, ItemTypeFilter.Folder, SelectionFilter.Single, null, true, "/command:cleanup /path:{folder}"),
                Definition("tsvn.repo-browser", "TortoiseSVN", "ToolPresetRepositoryBrowser", ToolExecutableRole.TortoiseSvn, "TortoiseProc.exe", ToolPresetAction.TortoiseSvnRepositoryBrowser, ItemTypeFilter.Folder, SelectionFilter.Single, null, false, "/command:repobrowser /path:{folder}"),
                Definition("7zip.open", "7-Zip", "ToolPresetSevenZipOpen", ToolExecutableRole.SevenZipFileManager, "7zFM.exe", ToolPresetAction.SevenZipOpen, ItemTypeFilter.File, SelectionFilter.Single, null, false, "7zFM.exe {path}"),
                Definition("7zip.extract-here", "7-Zip", "ToolPresetExtractHere", ToolExecutableRole.SevenZipGui, "7zG.exe", ToolPresetAction.SevenZipExtractHere, ItemTypeFilter.File, SelectionFilter.Single, archives, true, "7zG.exe x -y -o{parent} {path}"),
                Definition("7zip.extract-folder", "7-Zip", "ToolPresetExtractFolder", ToolExecutableRole.SevenZipGui, "7zG.exe", ToolPresetAction.SevenZipExtractFolder, ItemTypeFilter.File, SelectionFilter.Single, archives, true, "7zG.exe x -y -o{parent}\\{archiveBase} {path}"),
                Definition("7zip.compress-7z", "7-Zip", "ToolPresetCompressSevenZip", ToolExecutableRole.SevenZipGui, "7zG.exe", ToolPresetAction.SevenZipCompress, ItemTypeFilter.None, SelectionFilter.None, null, true, "7zG.exe a -t7z {archive}.7z {paths...}")
            };
        }

        private static ToolPresetDefinition Definition(string id, string groupName, string resourceKey, string role, string fileName, ToolPresetAction action, ItemTypeFilter itemType, SelectionFilter selection, string extensions, bool confirm, string summary)
        {
            return new ToolPresetDefinition(id, groupName, resourceKey, role, fileName, action, itemType, selection, extensions, confirm, summary);
        }

        private static void ValidateExecutable(ToolPresetDefinition definition, string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                throw new ToolPresetException(ToolPresetError.ExecutableMissing);
            if (!String.Equals(Path.GetFileName(executablePath), definition.ExpectedFileName, StringComparison.OrdinalIgnoreCase))
                throw new ToolPresetException(ToolPresetError.WrongExecutable);
        }

        private static void ValidateSelection(ToolPresetDefinition definition, IList<FileModel> items)
        {
            if (items.Count == 0)
                throw new ToolPresetException(ToolPresetError.SelectionRequired);
            if (definition.SelectionFilter == SelectionFilter.Single && items.Count != 1)
                throw new ToolPresetException(ToolPresetError.SingleSelectionRequired);
            if (definition.ItemTypeFilter == ItemTypeFilter.File && items.Any(x => x.IsDirectory))
                throw new ToolPresetException(ToolPresetError.FileRequired);
            if (definition.ItemTypeFilter == ItemTypeFilter.Folder && items.Any(x => !x.IsDirectory))
                throw new ToolPresetException(ToolPresetError.FolderRequired);
            if (!String.IsNullOrEmpty(definition.ExtensionFilter) && items.Any(x => !HasSupportedExtension(x, definition.ExtensionFilter)))
                throw new ToolPresetException(ToolPresetError.UnsupportedArchive);
            if (definition.Action == ToolPresetAction.SevenZipCompress && items.Any(x => !String.Equals(x.ParentPath, items[0].ParentPath, StringComparison.OrdinalIgnoreCase)))
                throw new ToolPresetException(ToolPresetError.DifferentParents);
        }

        private static bool HasSupportedExtension(FileModel item, string extensionFilter)
        {
            return extensionFilter.Split('|').Any(x => item.FullPath.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetWorkingDirectory(FileModel item)
        {
            return item.IsDirectory ? item.FullPath : item.ParentPath;
        }

        private static List<string> BuildArguments(ToolPresetAction action, IList<FileModel> items)
        {
            string path = items[0].FullPath;
            string parent = items[0].ParentPath;
            switch (action)
            {
                case ToolPresetAction.GitBash:
                    return Args("--cd=" + path);
                case ToolPresetAction.GitGui:
                    return Args("--working-dir", path);
                case ToolPresetAction.TortoiseGitCommit:
                    return Tortoise("commit", path);
                case ToolPresetAction.TortoiseGitSync:
                    return Tortoise("sync", path);
                case ToolPresetAction.TortoiseGitPull:
                    return Tortoise("pull", path);
                case ToolPresetAction.TortoiseGitPush:
                    return Tortoise("push", path);
                case ToolPresetAction.TortoiseGitLog:
                    return Tortoise("log", path);
                case ToolPresetAction.TortoiseGitDiff:
                    return Tortoise("diff", path);
                case ToolPresetAction.TortoiseSvnUpdate:
                    return Tortoise("update", path);
                case ToolPresetAction.TortoiseSvnCommit:
                    return Tortoise("commit", path);
                case ToolPresetAction.TortoiseSvnLog:
                    return Tortoise("log", path);
                case ToolPresetAction.TortoiseSvnDiff:
                    return Tortoise("diff", path);
                case ToolPresetAction.TortoiseSvnCleanup:
                    return Tortoise("cleanup", path);
                case ToolPresetAction.TortoiseSvnRepositoryBrowser:
                    return Tortoise("repobrowser", path);
                case ToolPresetAction.SevenZipOpen:
                    return Args(path);
                case ToolPresetAction.SevenZipExtractHere:
                    return Args("x", "-y", "-o" + parent, path);
                case ToolPresetAction.SevenZipExtractFolder:
                    return Args("x", "-y", "-o" + Path.Combine(parent, GetSingleArchiveBase(path)), path);
                case ToolPresetAction.SevenZipCompress:
                    string archivePath = Path.Combine(parent, GetArchiveBase(items) + ".7z");
                    return Args("a", "-t7z", archivePath).Concat(items.Select(x => x.FullPath)).ToList();
                default:
                    throw new ToolPresetException(ToolPresetError.UnknownPreset);
            }
        }

        private static string GetSingleArchiveBase(string path)
        {
            return Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private static string GetArchiveBase(IList<FileModel> items)
        {
            if (items.Count == 1)
                return items[0].IsDirectory
                    ? Path.GetFileName(items[0].FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : GetSingleArchiveBase(items[0].FullPath);

            string parent = items[0].ParentPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(parent);
            return String.IsNullOrWhiteSpace(name) ? "Archive" : name;
        }

        private static List<string> Tortoise(string command, string path)
        {
            return Args("/command:" + command, "/path:" + path);
        }

        private static List<string> Args(params string[] values)
        {
            return new List<string>(values);
        }
    }
}
