using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FileExplorer.Tools
{
    public sealed class ToolInvocation
    {
        public ToolInvocation(string application, string workingDirectory, IEnumerable<string> arguments, bool confirmBeforeRun)
        {
            Application = application;
            WorkingDirectory = workingDirectory;
            Arguments = new ReadOnlyCollection<string>(arguments.ToList());
            ConfirmBeforeRun = confirmBeforeRun;
        }

        public string Application { get; }

        public string WorkingDirectory { get; }

        public IReadOnlyList<string> Arguments { get; }

        public bool ConfirmBeforeRun { get; }
    }
}
