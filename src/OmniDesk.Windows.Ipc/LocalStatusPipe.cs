using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using OmniDesk.Contracts;

namespace OmniDesk.Windows.Ipc;

public static class LocalStatusPipe
{
    private const uint ClientRights = 0x00120083; // data read/write, read attributes, READ_CONTROL, synchronize; no create-instance.
    public static NamedPipeServerStream Create(int sessionId, SecurityIdentifier logonSid)
    {
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.SetOwner(system);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(logonSid, (PipeAccessRights)ClientRights, AccessControlType.Allow));
        var descriptor = security.GetSecurityDescriptorBinaryForm();
        var memory = Marshal.AllocHGlobal(descriptor.Length);
        try
        {
            Marshal.Copy(descriptor, 0, memory, descriptor.Length);
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = memory };
            // Duplex, overlapped, FIRST_PIPE_INSTANCE; byte mode + REJECT_REMOTE_CLIENTS.
            var handle = CreateNamedPipe(@"\\.\pipe\" + TrayProtocol.PipeName(sessionId), 0x40080003, 0x8, 1, 4096, 4096, 0, ref attributes);
            if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    // One server instance per logon also serves the status poll. Retry only transient
    // absence/contention; ownership, identity and access failures remain fail-closed.
    public static Task<NamedPipeClientStream> OpenVerifiedAsync(int sessionId, CancellationToken ct)
        => PipeConnectionRetry.Open(() => OpenVerified(sessionId), ct);

    public static NamedPipeClientStream OpenVerified(int sessionId)
    {
        var handle = CreateFile(@"\\.\pipe\" + TrayProtocol.PipeName(sessionId), ClientRights, 0, IntPtr.Zero, 3, 0x40110000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        var pipe = new NamedPipeClientStream(PipeDirection.InOut, true, true, handle);
        try
        {
            if (!GetNamedPipeServerProcessId(handle, out var pid) || pid == 0 || pid != ServiceProcessId()) throw new UnauthorizedAccessException("IPC server is not the registered Agent service.");
            if (!new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Equals(pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier))))
                throw new UnauthorizedAccessException("IPC owner is not SYSTEM.");
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OmniDesk Agent", "OmniDesk.Agent.Windows.exe");
            var registered = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\OmniDeskAgent", "ImagePath", null) as string;
            if (registered is null || !string.Equals(Environment.ExpandEnvironmentVariables(registered).Trim().Trim('"'), expected, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Unexpected Agent service image.");
            if (pid != ServiceProcessId()) throw new UnauthorizedAccessException("Agent service changed while connecting.");
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
    }

    private static uint ServiceProcessId()
    {
        var manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var service = OpenService(manager, "OmniDeskAgent", 4); // query status only
            if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatus>(), out _) || status.CurrentState != 4)
                    throw new IOException("Agent service is not running.");
                return status.ProcessId;
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type, CurrentState, Accepted, ExitCode, SpecificExit, Checkpoint, WaitHint, ProcessId, Flags; }
    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances, uint outSize, uint inSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafePipeHandle CreateFile(string name, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatusEx(IntPtr service, int level, out ServiceStatus status, int size, out int needed);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
}
