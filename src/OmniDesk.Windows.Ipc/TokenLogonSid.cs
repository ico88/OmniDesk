using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace OmniDesk.Windows.Ipc;

public static class TokenLogonSid
{
    // WindowsIdentity.Groups intentionally filters SE_GROUP_LOGON_ID.
    // TokenLogonSid returns TOKEN_GROUPS with the single logon SID (Windows 7+).
    public static SecurityIdentifier Read(SafeAccessTokenHandle token)
    {
        const int informationClass = 28;
        GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out var size);
        var error = Marshal.GetLastWin32Error();
        if (error != 122 || size < Marshal.SizeOf<TokenGroups>() || size > 65536)
            throw new Win32Exception(error, "Cannot size token logon SID.");
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(token, informationClass, memory, size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var groups = Marshal.PtrToStructure<TokenGroups>(memory);
            if (groups.Count != 1 || groups.First.Sid == IntPtr.Zero ||
                (groups.First.Attributes & 0xC0000000) != 0xC0000000)
                throw new UnauthorizedAccessException("Token has no unique logon SID.");
            var sid = new SecurityIdentifier(groups.First.Sid);
            if (!sid.IsWellKnown(WellKnownSidType.LogonIdsSid))
                throw new UnauthorizedAccessException("Unexpected logon SID.");
            return sid;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenGroups { public uint Count; public SidAndAttributes First; }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr data, int length, out int required);
}
