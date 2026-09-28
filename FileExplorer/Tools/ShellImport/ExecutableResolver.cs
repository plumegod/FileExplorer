using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FileExplorer.Tools.ShellImport
{
    public interface IExecutableResolver
    {
        IReadOnlyList<string> Resolve(string executable);
    }

    public sealed class WindowsExecutableResolver : IExecutableResolver
    {
        public IReadOnlyList<string> Resolve(string executable)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(executable))
                return Array.Empty<string>();

            if (Path.IsPathRooted(executable))
            {
                if (File.Exists(executable))
                    candidates.Add(Path.GetFullPath(executable));
                return new List<string>(candidates);
            }

            if (executable.IndexOfAny(new[] { '\\', '/' }) >= 0)
            {
                string relative = Path.GetFullPath(executable);
                if (File.Exists(relative))
                    candidates.Add(relative);
            }

            AddSearchPathCandidate(executable, candidates);
            AddEnvironmentPathCandidates(executable, candidates);
            AddAppPathCandidates(executable, candidates);
            return new List<string>(candidates);
        }

        private static void AddSearchPathCandidate(string executable, ISet<string> candidates)
        {
            var buffer = new StringBuilder(32768);
            uint length = SearchPath(null, executable, null, buffer.Capacity, buffer, IntPtr.Zero);
            if (length > 0 && length < buffer.Capacity && File.Exists(buffer.ToString()))
                candidates.Add(Path.GetFullPath(buffer.ToString()));
        }

        private static void AddEnvironmentPathCandidates(string executable, ISet<string> candidates)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
            foreach (string directory in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim().Trim('"'), executable);
                    if (File.Exists(candidate))
                        candidates.Add(Path.GetFullPath(candidate));
                }
                catch (Exception exception) when (exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is PathTooLongException)
                {
                }
            }
        }

        private static void AddAppPathCandidates(string executable, ISet<string> candidates)
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey key = baseKey.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + executable, false))
                        {
                            string value = key?.GetValue(String.Empty, null,
                                RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                            if (String.IsNullOrWhiteSpace(value))
                                continue;
                            string expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                            if (File.Exists(expanded))
                                candidates.Add(Path.GetFullPath(expanded));
                        }
                    }
                    catch (Exception exception) when (exception is UnauthorizedAccessException
                        || exception is System.Security.SecurityException
                        || exception is IOException)
                    {
                    }
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint SearchPath(
            string path,
            string fileName,
            string extension,
            int bufferLength,
            StringBuilder buffer,
            IntPtr filePart);
    }
}
