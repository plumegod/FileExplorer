using System;

namespace FileExplorer.Tools.ShellInvoke
{
    public static class ShellHandlerInvokerFactory
    {
        private static IShellHandlerInvoker current = new ShellHandlerInvoker();

        public static IShellHandlerInvoker Default
        {
            get => current;
            set => current = value ?? new ShellHandlerInvoker();
        }

        public static IDisposable Override(IShellHandlerInvoker invoker)
        {
            IShellHandlerInvoker previous = current;
            current = invoker ?? new ShellHandlerInvoker();
            return new OverrideScope(() => current = previous);
        }

        private sealed class OverrideScope : IDisposable
        {
            public OverrideScope(Action restore) { this.restore = restore; }
            public void Dispose() => restore();
            private readonly Action restore;
        }
    }
}
