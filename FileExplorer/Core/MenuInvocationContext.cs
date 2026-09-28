using System;
using System.Collections.Generic;
using System.Linq;
using FileExplorer.Model;

namespace FileExplorer.Core
{
    public enum MenuInvocationSource
    {
        Selection,
        Background
    }

    public sealed class MenuInvocationContext
    {
        public MenuInvocationContext(
            MenuInvocationSource source,
            IEnumerable<object> items,
            string workingDirectory)
        {
            Source = source;
            Items = (items ?? Enumerable.Empty<object>()).ToList();
            WorkingDirectory = workingDirectory;
        }

        public MenuInvocationSource Source { get; }
        public IList<object> Items { get; }
        public string WorkingDirectory { get; }

        public static MenuInvocationContext FromLegacy(IList<object> items)
        {
            List<FileModel> files = (items ?? Array.Empty<object>()).OfType<FileModel>().ToList();
            return new MenuInvocationContext(
                MenuInvocationSource.Selection,
                items,
                files.FirstOrDefault()?.ParentPath);
        }
    }
}
