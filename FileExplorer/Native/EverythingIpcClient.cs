using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileExplorer.Native
{
    internal class EverythingIpcClient
    {
        private const string NotificationWindowClass = "EVERYTHING_TASKBAR_NOTIFICATION";
        private const int CopyDataQueryUnicode = 2;
        private const int CopyDataQueryComplete = 0;
        private const int QueryHeaderSize = 20;
        private const int QueryTimeoutMilliseconds = 5000;
        private const uint EverythingAllResults = 0xFFFFFFFF;
        private const int WmCopyData = 0x004A;
        private const uint WmQuit = 0x0012;
        private const uint EverythingFolderFlag = 0x00000001;
        private const uint EverythingRootFlag = 0x00000002;

        private readonly string installDirectory;

        public EverythingIpcClient(string installDirectory)
        {
            this.installDirectory = installDirectory;
        }

        public static bool IsInstallDirectoryValid(string installDirectory)
        {
            if (String.IsNullOrWhiteSpace(installDirectory))
                return false;

            try
            {
                return File.Exists(GetExecutablePath(installDirectory));
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRunning()
        {
            return FindWindow(NotificationWindowClass, null) != IntPtr.Zero;
        }

        public static string GetExecutablePath(string installDirectory)
        {
            return Path.Combine(installDirectory, SearchEverything.ExecutableFileName);
        }

        public async Task<bool> EnsureRunning(CancellationToken cancellationToken = default)
        {
            if (IsRunning())
                return true;

            if (!IsInstallDirectoryValid(installDirectory))
                return false;

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = GetExecutablePath(installDirectory),
                    Arguments = "-startup",
                    WorkingDirectory = installDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
            }
            catch
            {
                return false;
            }

            for (int index = 0; index < 20; index++)
            {
                if (IsRunning())
                    return true;

                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            return IsRunning();
        }

        public FileSystemInfo[] Search(string query, CancellationToken cancellationToken = default)
        {
            IntPtr everythingWindowHandle = FindWindow(NotificationWindowClass, null);
            if (everythingWindowHandle == IntPtr.Zero)
                return null;

            QuerySession querySession = new QuerySession(everythingWindowHandle, query);
            return querySession.Execute(cancellationToken);
        }

        private class QuerySession
        {
            private readonly IntPtr everythingWindowHandle;
            private readonly string query;
            private readonly ManualResetEventSlim threadReadyEvent = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim completedEvent = new ManualResetEventSlim(false);

            private Exception error;
            private FileSystemInfo[] results;
            private int threadId;

            public QuerySession(IntPtr everythingWindowHandle, string query)
            {
                this.everythingWindowHandle = everythingWindowHandle;
                this.query = query ?? String.Empty;
            }

            public FileSystemInfo[] Execute(CancellationToken cancellationToken)
            {
                Thread thread = new Thread(ThreadMain)
                {
                    IsBackground = true
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                threadReadyEvent.Wait();

                if (cancellationToken.CanBeCanceled)
                {
                    int waitResult = WaitHandle.WaitAny(
                        new WaitHandle[] { completedEvent.WaitHandle, cancellationToken.WaitHandle },
                        QueryTimeoutMilliseconds);

                    if (waitResult != 0)
                    {
                        StopThread();
                        thread.Join(1000);
                        return null;
                    }
                }
                else if (!completedEvent.Wait(QueryTimeoutMilliseconds))
                {
                    StopThread();
                    thread.Join(1000);
                    return null;
                }

                thread.Join(1000);

                if (error != null)
                    throw error;

                return results;
            }

            private void ThreadMain()
            {
                threadId = GetCurrentThreadId();

                EverythingQueryWindow window = null;
                System.Windows.Forms.Timer timeoutTimer = null;

                try
                {
                    window = new EverythingQueryWindow(this);
                    timeoutTimer = new System.Windows.Forms.Timer
                    {
                        Interval = QueryTimeoutMilliseconds
                    };
                    timeoutTimer.Tick += (sender, e) =>
                    {
                        timeoutTimer.Stop();
                        results = null;
                        completedEvent.Set();
                        Application.ExitThread();
                    };

                    threadReadyEvent.Set();

                    if (!window.SendQuery(everythingWindowHandle, query))
                    {
                        results = null;
                        completedEvent.Set();
                        return;
                    }

                    timeoutTimer.Start();
                    Application.Run();
                }
                catch (Exception ex)
                {
                    error = ex;
                    completedEvent.Set();
                }
                finally
                {
                    if (timeoutTimer != null)
                    {
                        timeoutTimer.Stop();
                        timeoutTimer.Dispose();
                    }

                    if (window != null)
                        window.Dispose();

                    completedEvent.Set();
                }
            }

            private void StopThread()
            {
                if (threadId != 0)
                    PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            }

            private void SetResults(FileSystemInfo[] results)
            {
                this.results = results ?? Array.Empty<FileSystemInfo>();
                completedEvent.Set();
                Application.ExitThread();
            }

            private sealed class EverythingQueryWindow : NativeWindow, IDisposable
            {
                private readonly QuerySession owner;

                public EverythingQueryWindow(QuerySession owner)
                {
                    this.owner = owner;
                    CreateHandle(new CreateParams());
                }

                public void Dispose()
                {
                    if (Handle != IntPtr.Zero)
                        DestroyHandle();
                }

                public bool SendQuery(IntPtr everythingWindowHandle, string query)
                {
                    byte[] queryData = BuildQueryData(Handle, query);
                    IntPtr queryBuffer = Marshal.AllocHGlobal(queryData.Length);

                    try
                    {
                        Marshal.Copy(queryData, 0, queryBuffer, queryData.Length);

                        COPYDATASTRUCT copyData = new COPYDATASTRUCT
                        {
                            dwData = new IntPtr(CopyDataQueryUnicode),
                            cbData = queryData.Length,
                            lpData = queryBuffer
                        };

                        return SendMessage(everythingWindowHandle, WmCopyData, Handle, ref copyData) != IntPtr.Zero;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(queryBuffer);
                    }
                }

                protected override void WndProc(ref Message m)
                {
                    if (m.Msg == WmCopyData)
                    {
                        COPYDATASTRUCT copyData = (COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
                        if (copyData.dwData == new IntPtr(CopyDataQueryComplete))
                        {
                            owner.SetResults(ParseResults(copyData.lpData));
                            m.Result = new IntPtr(1);
                            return;
                        }
                    }

                    base.WndProc(ref m);
                }

                private static byte[] BuildQueryData(IntPtr replyHandle, string query)
                {
                    byte[] searchBuffer = Encoding.Unicode.GetBytes((query ?? String.Empty) + '\0');
                    byte[] queryData = new byte[QueryHeaderSize + searchBuffer.Length];

                    WriteUInt32(queryData, 0, unchecked((uint)replyHandle.ToInt64()));
                    WriteUInt32(queryData, 4, CopyDataQueryComplete);
                    WriteUInt32(queryData, 8, 0);
                    WriteUInt32(queryData, 12, 0);
                    WriteUInt32(queryData, 16, EverythingAllResults);

                    Buffer.BlockCopy(searchBuffer, 0, queryData, QueryHeaderSize, searchBuffer.Length);
                    return queryData;
                }

                private static FileSystemInfo[] ParseResults(IntPtr listPointer)
                {
                    if (listPointer == IntPtr.Zero)
                        return Array.Empty<FileSystemInfo>();

                    int numItems = Marshal.ReadInt32(listPointer, 20);
                    if (numItems <= 0)
                        return Array.Empty<FileSystemInfo>();

                    FileSystemInfo[] items = new FileSystemInfo[numItems];

                    for (int index = 0; index < numItems; index++)
                    {
                        int itemOffset = 28 + index * 12;
                        uint flags = unchecked((uint)Marshal.ReadInt32(listPointer, itemOffset));
                        int fileNameOffset = Marshal.ReadInt32(listPointer, itemOffset + 4);
                        int pathOffset = Marshal.ReadInt32(listPointer, itemOffset + 8);

                        string fileName = Marshal.PtrToStringUni(IntPtr.Add(listPointer, fileNameOffset)) ?? String.Empty;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(listPointer, pathOffset)) ?? String.Empty;
                        string fullPath = GetFullPath(path, fileName);

                        if (String.IsNullOrWhiteSpace(fullPath))
                            continue;

                        if ((flags & (EverythingFolderFlag | EverythingRootFlag)) != 0)
                            items[index] = new DirectoryInfo(fullPath);
                        else
                            items[index] = new FileInfo(fullPath);
                    }

                    return items;
                }

                private static string GetFullPath(string path, string fileName)
                {
                    if (String.IsNullOrEmpty(path))
                        return fileName;

                    if (String.IsNullOrEmpty(fileName))
                        return path;

                    return Path.Combine(path, fileName);
                }

                private static void WriteUInt32(byte[] buffer, int offset, uint value)
                {
                    byte[] bytes = BitConverter.GetBytes(value);
                    Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();
    }
}
