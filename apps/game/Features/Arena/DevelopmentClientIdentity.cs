using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Arvenok.Hosting;

namespace Arvenok.Game.Features.Arena;

/// <summary>Local profile selector and opaque server-issued reconnect credential; never character state.</summary>
internal sealed class DevelopmentClientIdentity : IDisposable
{
    private readonly FileStream _lease;
    private readonly string _credential;
    public string Profile { get; }
    public string ResumeToken { get; private set; }
    public DevelopmentClientIdentity(string root, SessionOptions options)
    {
        string endpoint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.Host + ":" + options.Port)));
        string directory = Path.Combine(root, ".local", "client", endpoint); Directory.CreateDirectory(directory);
        FileStream? lease = null; string profile = options.Profile;
        for (int index = 1; index <= 32; index++)
        {
            profile = options.Profile == "auto" ? "client-" + index : options.Profile;
            try { lease = new FileStream(Path.Combine(directory, profile + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); break; }
            catch (IOException) when (options.Profile == "auto") { }
        }
        _lease = lease ?? throw new IOException("All development profiles are open; choose a separate --profile.");
        Profile = profile; _credential = Path.Combine(directory, profile + ".credential");
        try
        {
            ResumeToken = File.Exists(_credential) ? File.ReadAllText(_credential).Trim() : "";
            if (ResumeToken.Length > 0) _ = JsonCharacterRepository.TokenHash(ResumeToken);
        }
        catch { _lease.Dispose(); throw; }
    }
    public void Remember(string token)
    {
        _ = JsonCharacterRepository.TokenHash(token);
        string temporary = _credential + ".tmp";
        File.WriteAllText(temporary, token, new UTF8Encoding(false)); File.Move(temporary, _credential, overwrite: true);
        ResumeToken = token;
    }
    public void Dispose() => _lease.Dispose();
}
