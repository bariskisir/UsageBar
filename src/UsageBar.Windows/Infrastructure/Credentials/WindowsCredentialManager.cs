using System.Runtime.InteropServices;
using System.Text;
using UsageBar.Windows.Tray;

namespace UsageBar.Windows.Infrastructure;

internal static class WindowsCredentialManager
{
    private const int ERROR_NOT_FOUND = 1168;

    public static string? Read(string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!NativeMethods.CredRead(target, NativeMethods.CredentialType.Generic, 0, out var ptr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ERROR_NOT_FOUND)
            {
                return null;
            }

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

            if (blob.Length >= 2 && blob[0] == 0xFF && blob[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(blob).TrimEnd('\0');
            }

            return Encoding.UTF8.GetString(blob).TrimEnd('\0');
        }
        finally
        {
            NativeMethods.CredFree(ptr);
        }
    }

    public static bool Write(string target, string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var blob = Encoding.UTF8.GetBytes(value);
        var cred = new NativeMethods.Credential
        {
            Type = (uint)NativeMethods.CredentialType.Generic,
            TargetName = Marshal.StringToCoTaskMemUni(target),
            CredentialBlobSize = (uint)blob.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(blob.Length),
            Persist = (uint)NativeMethods.CredentialPersistence.Enterprise,
            UserName = Marshal.StringToCoTaskMemUni(string.Empty),
        };

        try
        {
            Marshal.Copy(blob, 0, cred.CredentialBlob, blob.Length);
            return NativeMethods.CredWrite(ref cred, 0);
        }
        finally
        {
            Marshal.FreeCoTaskMem(cred.TargetName);
            Marshal.FreeCoTaskMem(cred.CredentialBlob);
            Marshal.FreeCoTaskMem(cred.UserName);
        }
    }
}