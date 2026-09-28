using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Shell32;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace FileExplorer.Tools.ShellInvoke
{
    public enum ShellHandlerInvocationStatus
    {
        Succeeded,
        Cancelled,
        Failed
    }

    public sealed class ShellHandlerInvocationRequest
    {
        public ShellHandlerInvocationRequest(
            Guid clsid,
            IReadOnlyList<string> paths,
            string workingDirectory = null,
            IntPtr ownerHwnd = default,
            Point? popupPoint = null)
        {
            Clsid = clsid;
            Paths = paths ?? Array.Empty<string>();
            WorkingDirectory = workingDirectory;
            OwnerHwnd = ownerHwnd;
            PopupPoint = popupPoint;
        }

        public Guid Clsid { get; }
        public IReadOnlyList<string> Paths { get; }
        public string WorkingDirectory { get; }
        public IntPtr OwnerHwnd { get; }
        public Point? PopupPoint { get; }
    }

    public sealed class ShellHandlerInvocationResult
    {
        public ShellHandlerInvocationResult(ShellHandlerInvocationStatus status, string errorCode = null, string message = null)
        {
            Status = status;
            ErrorCode = errorCode;
            Message = message;
        }

        public ShellHandlerInvocationStatus Status { get; }
        public string ErrorCode { get; }
        public string Message { get; }

        public static ShellHandlerInvocationResult Succeeded()
            => new ShellHandlerInvocationResult(ShellHandlerInvocationStatus.Succeeded);

        public static ShellHandlerInvocationResult Cancelled()
            => new ShellHandlerInvocationResult(ShellHandlerInvocationStatus.Cancelled);

        public static ShellHandlerInvocationResult Failed(string errorCode, string message = null)
            => new ShellHandlerInvocationResult(ShellHandlerInvocationStatus.Failed, errorCode, message);
    }

    public interface IShellHandlerInvoker
    {
        ShellHandlerInvocationResult Invoke(ShellHandlerInvocationRequest request);
    }

    public sealed class ShellHandlerInvoker : IShellHandlerInvoker
    {
        private static int activeInvocations;

        public ShellHandlerInvocationResult Invoke(ShellHandlerInvocationRequest request)
        {
            if (request == null)
                return ShellHandlerInvocationResult.Failed("request-null");
            if (request.Clsid == Guid.Empty)
                return ShellHandlerInvocationResult.Failed("clsid-empty");

            string[] paths = request.Paths?
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            if (paths.Length == 0)
                return ShellHandlerInvocationResult.Failed("paths-empty");

            if (Interlocked.CompareExchange(ref activeInvocations, 1, 0) != 0)
                return ShellHandlerInvocationResult.Failed("reentrant");

            object comObject = null;
            try
            {
                Type type;
                try
                {
                    type = Type.GetTypeFromCLSID(request.Clsid, throwOnError: true);
                }
                catch (Exception ex)
                {
                    return ShellHandlerInvocationResult.Failed("clsid-create-type", ex.Message);
                }

                try
                {
                    comObject = Activator.CreateInstance(type);
                }
                catch (Exception ex)
                {
                    return ShellHandlerInvocationResult.Failed("clsid-create-instance", ex.Message);
                }

                if (!(comObject is IShellExtInit shellExtInit))
                    return ShellHandlerInvocationResult.Failed("not-ishellextinit");
                if (!(comObject is IContextMenu contextMenu))
                    return ShellHandlerInvocationResult.Failed("not-icontextmenu");

                ShellItem[] shellItems;
                try
                {
                    shellItems = paths.Select(path => new ShellItem(path)).ToArray();
                }
                catch (Exception ex)
                {
                    return ShellHandlerInvocationResult.Failed("shellitem-create", ex.Message);
                }

                var dataObject = new ShellDataObject(shellItems);
                ComIDataObject comDataObject = dataObject;
                HRESULT initResult = shellExtInit.Initialize(default(PIDL), comDataObject, default(HKEY));
                if (initResult.Failed)
                    return ShellHandlerInvocationResult.Failed("initialize-failed", initResult.ToString());

                HWND owner = request.OwnerHwnd == IntPtr.Zero
                    ? default(HWND)
                    : new HWND(request.OwnerHwnd);

                Point screenPoint = request.PopupPoint
                    ?? (Application.Current?.MainWindow != null
                        ? Application.Current.MainWindow.PointToScreen(new Point(16, 16))
                        : new Point(0, 0));

                var shellContextMenu = new ShellContextMenu(contextMenu);
                try
                {
                    shellContextMenu.ShowContextMenu(
                        new POINT((int)screenPoint.X, (int)screenPoint.Y),
                        CMF.CMF_NORMAL | CMF.CMF_EXPLORE | CMF.CMF_CANRENAME,
                        null,
                        owner);
                    return ShellHandlerInvocationResult.Succeeded();
                }
                catch (COMException ex) when (unchecked((uint)ex.ErrorCode) == 0x800704C7)
                {
                    return ShellHandlerInvocationResult.Cancelled();
                }
                catch (Exception ex)
                {
                    return ShellHandlerInvocationResult.Failed("show-menu-failed", ex.Message);
                }
            }
            finally
            {
                if (comObject != null && Marshal.IsComObject(comObject))
                {
                    try { Marshal.FinalReleaseComObject(comObject); }
                    catch { }
                }
                Interlocked.Exchange(ref activeInvocations, 0);
            }
        }
    }
}
