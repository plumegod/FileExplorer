using System.Collections.Generic;
using FileExplorer.Model;
using FileExplorer.Persistence;
using FileExplorer.Tools;

namespace FileExplorer.Messages
{
    public class CommandMessage
    {
        public string Arguments { get; set; }

        public string Directory { get; set; }

        public MenuItem MenuItem { get; set; }

        public IEnumerable<FileModel> Parameters { get; set; }

        public ToolInvocation Invocation { get; set; }

        public string ExecutionError { get; set; }
    }
}
