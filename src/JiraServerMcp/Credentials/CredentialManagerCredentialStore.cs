using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using JiraServerMcp.Configuration;

namespace JiraServerMcp.Credentials;

/// <summary>
/// Windows Credential Manager, through `CredRead` / `CredWrite` / `CredDelete`. This is the one
/// backend not driven by a platform tool: `cmdkey` writes and deletes a generic credential but
/// will never print one back, so a shelled-out Windows store could store a token and then not
/// retrieve it. The design allows for that (architecture §6), and the interop is three calls
/// against a stable Win32 API rather than a native library to build per runtime identifier.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CredentialManagerCredentialStore : INativeCredentialStore
{
    private const int GenericCredential = 1;

    /// <summary>
    /// This machine, this user, not roamed to any other — the same reach the other backends have.
    /// </summary>
    private const int PersistLocalMachine = 2;

    private const int ErrorNotFound = 1168;

    public Task<string?> GetAsync(string profileName, CancellationToken cancellationToken)
    {
        if (!CredRead(Target(profileName), GenericCredential, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();

            return error is ErrorNotFound
                ? Task.FromResult<string?>(null)
                : throw Unreachable(error, $"read the credential for profile '{profileName}'");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);
            var blob = new byte[credential.CredentialBlobSize];

            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);

            return Task.FromResult<string?>(Encoding.Unicode.GetString(blob));
        }
        finally
        {
            CredFree(handle);
        }
    }

    public Task SetAsync(
        string profileName,
        string personalAccessToken,
        CancellationToken cancellationToken)
    {
        var blob = Encoding.Unicode.GetBytes(personalAccessToken);
        var blobHandle = Marshal.AllocHGlobal(blob.Length);
        var targetHandle = Marshal.StringToHGlobalUni(Target(profileName));
        var userNameHandle = Marshal.StringToHGlobalUni(profileName);

        try
        {
            Marshal.Copy(blob, 0, blobHandle, blob.Length);

            var credential = new Credential
            {
                Type = GenericCredential,
                TargetName = targetHandle,
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobHandle,
                Persist = PersistLocalMachine,
                UserName = userNameHandle,
            };

            if (!CredWrite(ref credential, 0))
            {
                throw Unreachable(
                    Marshal.GetLastWin32Error(),
                    $"store the credential for profile '{profileName}'");
            }

            return Task.CompletedTask;
        }
        finally
        {
            // The token was copied into unmanaged memory, so it is scrubbed rather than merely
            // released.
            Array.Clear(blob);
            Marshal.Copy(blob, 0, blobHandle, blob.Length);
            Marshal.FreeHGlobal(blobHandle);
            Marshal.FreeHGlobal(targetHandle);
            Marshal.FreeHGlobal(userNameHandle);
        }
    }

    public Task DeleteAsync(string profileName, CancellationToken cancellationToken)
    {
        if (CredDelete(Target(profileName), GenericCredential, 0))
        {
            return Task.CompletedTask;
        }

        var error = Marshal.GetLastWin32Error();

        return error is ErrorNotFound
            ? Task.CompletedTask
            : throw Unreachable(error, $"remove the credential for profile '{profileName}'");
    }

    public string Describe() =>
        $"Windows Credential Manager, under the target '{NativeCredentialStore.Service}:<profile>'";

    public Task<bool> IsUsableAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (CredRead(Target(" probe"), GenericCredential, 0, out var handle))
            {
                CredFree(handle);

                return Task.FromResult(true);
            }

            // The probe credential cannot exist, so the reachable answer is the missing one.
            return Task.FromResult(Marshal.GetLastWin32Error() is ErrorNotFound);
        }
        catch (DllNotFoundException)
        {
            return Task.FromResult(false);
        }
        catch (EntryPointNotFoundException)
        {
            return Task.FromResult(false);
        }
    }

    private static string Target(string profileName) =>
        $"{NativeCredentialStore.Service}:{profileName}";

    private static ConfigurationException Unreachable(int error, string attempt) =>
        new($"Windows Credential Manager could not {attempt}: error {error}. Use "
            + "'--credential-store file' to keep credentials in an encrypted file instead.");

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
