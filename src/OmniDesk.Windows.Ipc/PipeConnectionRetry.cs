using System.ComponentModel;

namespace OmniDesk.Windows.Ipc;

internal static class PipeConnectionRetry
{
    public static async Task<T> Open<T>(Func<T> open, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return open(); }
            catch (Win32Exception e) when (e.NativeErrorCode is 2 or 231)
            { await Task.Delay(100, ct); }
        }
    }
}
