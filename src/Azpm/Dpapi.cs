using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Azpm;

/// <summary>
/// Windows DPAPI (<c>CryptProtectData</c>) — per-user at-rest encryption with no key management.
/// Only meaningful on Windows; callers guard with <see cref="OperatingSystem.IsWindows"/>.
/// </summary>
internal static partial class Dpapi
{
    public static byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);

    public static byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        var handle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var outBlob = default(DataBlob);
        try
        {
            var inBlob = new DataBlob { cbData = input.Length, pbData = handle.AddrOfPinnedObject() };
            var ok = protect
                ? CryptProtectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);
            if (!ok)
                throw new Win32Exception(Marshal.GetLastPInvokeError(),
                    $"DPAPI {(protect ? "protect" : "unprotect")} failed");

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        ref DataBlob pDataIn, IntPtr szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
        IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr hMem);
}
