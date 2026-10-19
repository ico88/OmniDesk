using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace OmniDesk.Agent.Windows;

internal sealed record InteractiveLogon(int SessionId, string UserSid, string LogonSid);
internal static class InteractiveLogons
{
    public static IReadOnlyList<InteractiveLogon> Read()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var memory, out var count)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new List<InteractiveLogon>();
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (result.Count >= 128) break;
                var info = Marshal.PtrToStructure<SessionInfo>(IntPtr.Add(memory, i * Marshal.SizeOf<SessionInfo>()));
                if (info.Id <= 0 || !WTSQueryUserToken((uint)info.Id, out var token)) continue;
                using (token)
                using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
                {
                    var sid = identity.Groups?.OfType<SecurityIdentifier>().SingleOrDefault(g => g.IsWellKnown(WellKnownSidType.LogonIdsSid));
                    if (identity.User is not null && sid is not null) result.Add(new(info.Id, identity.User.Value, sid.Value));
                }
            }
        }
        finally { WTSFreeMemory(memory); }
        return result;
    }
    [StructLayout(LayoutKind.Sequential)] private struct SessionInfo { public int Id; public IntPtr Name; public int State; }
    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)] private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(uint sessionId, out SafeAccessTokenHandle token);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
}
