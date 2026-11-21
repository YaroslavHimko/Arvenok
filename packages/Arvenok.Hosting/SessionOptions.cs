using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Arvenok.Hosting;

/// <summary>Process configuration, independent of Godot and any cloud provider.</summary>
public sealed record SessionOptions(bool IsServer, string Host, string BindAddress, int Port, string Region, string Shard)
{
    public string SmokeRole { get; init; } = "none";
    public string Profile { get; init; } = "auto";
    public string DataDirectory { get; init; } = "";
    public Guid ResetSkills { get; init; }
    public Guid GrantInventory { get; init; }
    public static SessionOptions Parse(IEnumerable<string> arguments, Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool server = false;
        using IEnumerator<string> iterator = arguments.GetEnumerator();
        while (iterator.MoveNext())
        {
            string argument = iterator.Current;
            if (argument == "--server")
            {
                if (server) throw new ArgumentException("Duplicate --server option.");
                server = true;
                continue;
            }
            if (argument is not ("--host" or "--bind" or "--port" or "--region" or "--shard" or "--smoke-role" or "--profile" or "--data-dir" or "--reset-skills" or "--grant-inventory"))
                throw new ArgumentException($"Unknown session option: {argument}");
            if (!iterator.MoveNext() || iterator.Current.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {argument}.");
            if (!values.TryAdd(argument, iterator.Current))
                throw new ArgumentException($"Duplicate session option: {argument}");
        }

        string Read(string option, string environment, string fallback)
            => values.TryGetValue(option, out string? value) ? value : readEnvironment(environment) ?? fallback;

        string host = Read("--host", "ARVENOK_HOST", "127.0.0.1");
        string bind = Read("--bind", "ARVENOK_BIND", "127.0.0.1");
        string portText = Read("--port", "ARVENOK_PORT", "7777");
        string region = Read("--region", "ARVENOK_REGION", "local");
        string shard = Read("--shard", "ARVENOK_SHARD", "world-001");
        string smokeRole = values.GetValueOrDefault("--smoke-role", "none");
        if (smokeRole is not ("none" or "attacker" or "defender" or "progression-a" or "progression-b" or "progression-restore" or "inventory" or "inventory-restore")) throw new ArgumentException("Invalid smoke role.");
        string profile = Read("--profile", "ARVENOK_PROFILE", "auto"); ValidateLabel(profile, "Profile");
        string data = Read("--data-dir", "ARVENOK_DATA_DIR", "");
        if (data.Contains('\0') || data.Length > 1024) throw new ArgumentException("Invalid development save directory.");
        Guid reset = Guid.Empty;
        if (values.TryGetValue("--reset-skills", out string? resetText) && (!server || !Guid.TryParse(resetText, out reset) || reset == Guid.Empty))
            throw new ArgumentException("--reset-skills requires --server and a valid saved character ID.");
        Guid grant = Guid.Empty;
        if (values.TryGetValue("--grant-inventory", out string? grantText) && (!server || !Guid.TryParse(grantText, out grant) || grant == Guid.Empty || reset != Guid.Empty))
            throw new ArgumentException("--grant-inventory requires --server, a character GUID, and no reset operation.");
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("Host must be a hostname or IP address.");
        if (!IPAddress.TryParse(bind, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Bind address must be an IPv4 address (use 0.0.0.0 for all interfaces).");
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
            throw new ArgumentException("Port must be an integer between 1 and 65535.");
        ValidateLabel(region, "Region");
        ValidateLabel(shard, "Shard");
        return new SessionOptions(server, host, bind, port, region, shard) { SmokeRole = smokeRole, Profile = profile, DataDirectory = data, ResetSkills = reset, GrantInventory = grant };
    }

    private static void ValidateLabel(string value, string label)
    {
        if (value.Length is < 1 or > 63)
            throw new ArgumentException($"{label} must contain 1 to 63 lowercase letters, digits, or hyphens.");
        foreach (char character in value)
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                throw new ArgumentException($"{label} must contain only lowercase letters, digits, or hyphens.");
    }
}
