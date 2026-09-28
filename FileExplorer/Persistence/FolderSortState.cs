using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FileExplorer.Persistence
{
    public class FolderSortState : PersistentItem
    {
        [Required]
        public string FolderPath { get; set; }

        [Required]
        public string FolderPathKey { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public List<FolderSortColumnState> Columns { get; set; } = new List<FolderSortColumnState>();

        public static string GetFolderPathKey(string folderPath)
        {
            return folderPath?.ToUpperInvariant();
        }
    }

    public class FolderSortColumnState
    {
        public string FieldName { get; set; }

        public int SortOrder { get; set; }
    }
}
