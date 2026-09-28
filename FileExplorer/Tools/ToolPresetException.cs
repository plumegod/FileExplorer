using System;
using System.Collections.Generic;

namespace FileExplorer.Tools
{
    public sealed class ToolPresetException : Exception
    {
        public ToolPresetException(string errorCode)
            : base(errorCode)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }

    public static class ToolPresetError
    {
        public const string UnknownPreset = "UnknownPreset";
        public const string ExecutableMissing = "ExecutableMissing";
        public const string WrongExecutable = "WrongExecutable";
        public const string SelectionRequired = "SelectionRequired";
        public const string SingleSelectionRequired = "SingleSelectionRequired";
        public const string FileRequired = "FileRequired";
        public const string FolderRequired = "FolderRequired";
        public const string UnsupportedArchive = "UnsupportedArchive";
        public const string DifferentParents = "DifferentParents";
        public const string WorkingDirectoryMissing = "WorkingDirectoryMissing";

        public static IReadOnlyList<string> All { get; } = new[]
        {
            UnknownPreset,
            ExecutableMissing,
            WrongExecutable,
            SelectionRequired,
            SingleSelectionRequired,
            FileRequired,
            FolderRequired,
            UnsupportedArchive,
            DifferentParents,
            WorkingDirectoryMissing
        };
    }

    public static class ToolPresetErrorText
    {
        public static string Get(string errorCode)
        {
            string text = FileExplorer.Properties.Resources.ResourceManager.GetString("ToolPresetError" + errorCode);
            return String.IsNullOrWhiteSpace(text)
                ? String.Format(FileExplorer.Properties.Resources.ToolPresetErrorFormat, errorCode)
                : text;
        }
    }
}
