using System;
using System.IO;
using Arvenok.Hosting;
using Godot;

namespace Arvenok.Game.Features.Arena;
public partial class ArenaSession
{
    private SessionOptions _options = null!;
    private WebSocketMultiplayerPeer SecurePeer => (WebSocketMultiplayerPeer)_peer!;
    private Error OpenSecurePeer()
    {
        try { return CreateSecurePeer(); }
        catch (Exception)
        {
            Status = "Secure connection unavailable. Check the certificate, key and server address.";
            GD.PushError("TLS setup failed. Check configured certificate paths and permissions.");
            return Error.CantConnect;
        }
    }
    private Error CreateSecurePeer()
    {
        _peer?.Close();
        var peer = new WebSocketMultiplayerPeer { HandshakeTimeout = 8, InboundBufferSize = 1048576, OutboundBufferSize = 1048576 };
        _peer = peer;
        string folder = Path.Combine(RepositoryDirectory(), ".local", "tls");
        bool local = _options.Host is "127.0.0.1" or "localhost";
        string? certificatePath = System.Environment.GetEnvironmentVariable("ARVENOK_TLS_CERT");
        string? keyPath = System.Environment.GetEnvironmentVariable("ARVENOK_TLS_KEY");
        Error result;
        if (_options.IsServer)
        {
            if (certificatePath is null && keyPath is null && _options.BindAddress == "127.0.0.1")
            {
                DevelopmentTls.Create(folder);
                certificatePath = Path.Combine(folder, "localhost.crt"); keyPath = Path.Combine(folder, "localhost.key");
            }
            if (certificatePath is null || keyPath is null) throw new InvalidOperationException("Set ARVENOK_TLS_CERT and ARVENOK_TLS_KEY for this bind address.");
            var certificate = new X509Certificate(); var key = new CryptoKey();
            if (certificate.Load(certificatePath) != Error.Ok || key.Load(keyPath) != Error.Ok) throw new InvalidOperationException("Cannot load server TLS certificate or key.");
            result = peer.CreateServer(_options.Port, _options.BindAddress, TlsOptions.Server(key, certificate));
        }
        else
        {
            string? trustPath = System.Environment.GetEnvironmentVariable("ARVENOK_TLS_CA");
            if (trustPath is null && local && File.Exists(Path.Combine(folder, "localhost.crt"))) trustPath = Path.Combine(folder, "localhost.crt");
            X509Certificate? trusted = null;
            if (trustPath is not null) { trusted = new X509Certificate(); if (trusted.Load(trustPath) != Error.Ok) throw new InvalidOperationException("Cannot load the trusted TLS certificate."); }
            result = peer.CreateClient($"wss://{_options.Host}:{_options.Port}", TlsOptions.Client(trusted));
        }
        if (result == Error.Ok) Multiplayer.MultiplayerPeer = peer;
        return result;
    }
    public void ReconnectAccount()
    {
        if (_simulation is not null) return;
        ClearWorldPresentation(); AccountState = null; AccountBusy = false;
        try { Status = OpenSecurePeer() == Error.Ok ? "Connecting securely..." : "Server unavailable. Check the address and retry."; }
        catch (Exception) { Status = "Secure connection unavailable. Check the server and trusted certificate."; }
    }
}
