using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Telenec.Mail.Statistics.Admin.Security;

public sealed class WindowsAdminApiKeyStore : IAdminApiKeyStore
{
    private const string TargetName =
        "Telenec.Mail.Statistics.Admin:AdminApiKey";

    private const string CredentialUserName =
        "Telenec.Mail.Statistics.Admin";

    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public Task<string?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CredRead(
                TargetName,
                CredentialTypeGeneric,
                0,
                out var credentialPointer))
        {
            var error =
                Marshal.GetLastWin32Error();

            if (error == ErrorNotFound)
            {
                return Task.FromResult<string?>(null);
            }

            throw new Win32Exception(error);
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<NativeCredential>(
                    credentialPointer);

            var apiKey =
                credential.CredentialBlob == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUni(
                        credential.CredentialBlob,
                        checked(
                            (int)credential.CredentialBlobSize / 2))
                      ?? string.Empty;

            return Task.FromResult<string?>(
                apiKey);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task SaveAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var apiKeyPointer =
            Marshal.StringToCoTaskMemUni(apiKey);

        try
        {
            var credential =
                new NativeCredential
                {
                    Type =
                        CredentialTypeGeneric,

                    TargetName =
                        TargetName,

                    UserName =
                        CredentialUserName,

                    CredentialBlob =
                        apiKeyPointer,

                    CredentialBlobSize =
                        checked(
                            (uint)(apiKey.Length * sizeof(char))),

                    Persist =
                        CredentialPersistLocalMachine
                };

            if (!CredWrite(
                    ref credential,
                    0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            return Task.CompletedTask;
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(
                apiKeyPointer);
        }
    }

    public Task DeleteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (CredDelete(
                TargetName,
                CredentialTypeGeneric,
                0))
        {
            return Task.CompletedTask;
        }

        var error =
            Marshal.GetLastWin32Error();

        if (error == ErrorNotFound)
        {
            return Task.CompletedTask;
        }

        throw new Win32Exception(error);
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public FILETIME LastWritten;

        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;

        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredWriteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref NativeCredential credential,
        uint flags);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredReadW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPtr);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredDeleteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string target,
        uint type,
        uint flags);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredFree")]
    private static extern void CredFree(
        IntPtr buffer);
}