using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Loans.Web.OAuth;

/// <summary>Loads an RSA key from a private PEM file, generating it (owner-only) on first run.</summary>
public static class PersistedRsaKeys
{
    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerOnlyDirectory = OwnerOnlyFile | UnixFileMode.UserExecute;

    public static RsaSecurityKey LoadOrCreate(string path)
    {
        EnsurePrivateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            using var generated = RSA.Create(2048);
            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                fileOptions.UnixCreateMode = OwnerOnlyFile;
            }

            using (var stream = new FileStream(temp, fileOptions))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(generated.ExportPkcs8PrivateKeyPem());
            }

            try
            {
                File.Move(temp, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                File.Delete(temp); // Another instance created it first; use theirs.
            }
        }

        var rsa = RSA.Create(); // Lives for the process: owned by the singleton security key.
        rsa.ImportFromPem(File.ReadAllText(path));

        var key = new RsaSecurityKey(rsa);
        key.KeyId = Base64UrlEncoder.Encode(key.ComputeJwkThumbprint());
        return key;
    }

    public static void EnsurePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, OwnerOnlyDirectory);
        }
    }
}
