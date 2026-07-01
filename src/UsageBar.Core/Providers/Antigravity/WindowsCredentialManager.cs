using System.Runtime.InteropServices;
using System.Text;

namespace UsageBar.Providers;

internal static class WindowsCredentialManager
{
    private const string Advapi32 = "advapi32.dll";

    public static string? Read(string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!CredRead(target, CRED_TYPE.GENERIC, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"CredRead failed for target '{target}' with error code {error} (0x{error:X8}).");
        }

        try
        {
            var is64 = IntPtr.Size == 8;
            var blobSizeOffset = is64 ? 32 : 24;
            var blobPtrOffset = is64 ? 40 : 28;

            var blobSize = (int)Marshal.ReadInt32(ptr, blobSizeOffset);
            var blobPtr = Marshal.ReadIntPtr(ptr, blobPtrOffset);

            if (blobSize == 0 || blobPtr == IntPtr.Zero)
            {
                return null;
            }

            var blob = new byte[blobSize];
            Marshal.Copy(blobPtr, blob, 0, blobSize);

            // Gemini CLI stores as UTF-8. Fall back to UTF-16-LE on detectable BOM.
            if (blob.Length >= 2 && blob[0] == 0xFF && blob[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(blob).TrimEnd('\0');
            }

            return Encoding.UTF8.GetString(blob).TrimEnd('\0');
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public static bool Write(string target, string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var blob = Encoding.UTF8.GetBytes(value);
        var cred = new CREDENTIAL
        {
            Type = (uint)CRED_TYPE.GENERIC,
            TargetName = Marshal.StringToCoTaskMemUni(target),
            CredentialBlobSize = (uint)blob.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(blob.Length),
            Persist = (uint)CRED_PERSIST.ENTERPRISE,
            UserName = Marshal.StringToCoTaskMemUni(string.Empty),
        };

        try
        {
            Marshal.Copy(blob, 0, cred.CredentialBlob, blob.Length);
            return CredWrite(ref cred, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(cred.TargetName);
            Marshal.FreeCoTaskMem(cred.CredentialBlob);
            Marshal.FreeCoTaskMem(cred.UserName);
        }
    }

    [DllImport(Advapi32, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, CRED_TYPE type, uint flags, out IntPtr credential);

    [DllImport(Advapi32, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport(Advapi32, SetLastError = true)]
    private static extern bool CredFree(IntPtr cred);

    private enum CRED_TYPE : uint { GENERIC = 1 }

    private enum CRED_PERSIST : uint { SESSION = 1, LOCAL_MACHINE = 2, ENTERPRISE = 3 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
