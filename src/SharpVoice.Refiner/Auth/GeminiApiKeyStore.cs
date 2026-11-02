using System.Security.Cryptography;
using System.Text;
using SharpVoice.Core;

namespace SharpVoice.Refiner.Auth;

/// <summary>Separate from preferences/history; only the current Windows user can decrypt it.</summary>
public sealed class GeminiApiKeyStore(string? path = null)
{
    private readonly string file = path ?? Path.Combine(AppPaths.Root, "gemini-api-key.dpapi");
    public bool IsConfigured => File.Exists(file);
    public static string Validate(string key)
    {
        var value = key.Trim();
        if (value.Length is < 8 or > 4096 || value.Any(c => c is <= ' ' or > '~'))
            throw new InvalidOperationException("Enter a valid Google AI Studio API key without spaces or line breaks.");
        return value;
    }
    public string Load()
    {
        if (!File.Exists(file)) return "";
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows encryption is required for saved API keys.");
        byte[]? plaintext = null;
        try
        {
            if (new FileInfo(file).Length > 16384) throw new InvalidDataException();
            plaintext = ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser);
            return Validate(Encoding.UTF8.GetString(plaintext));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        { throw new InvalidOperationException("The saved Gemini API key cannot be read. Open Manage API Key and replace it for this Windows user."); }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }
    public void Save(string key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows encryption is required for saved API keys.");
        var value = Validate(key);
        var plaintext = Encoding.UTF8.GetBytes(value);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { throw new InvalidOperationException("Could not securely save the Gemini API key. Check access to SharpVoice's settings folder and retry."); }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    public void Remove() => File.Delete(file);
}
