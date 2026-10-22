using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;
using AlienCommandCenter.Models;

namespace AlienCommandCenter.Services;

public sealed class ActiveLightingStore
{
    private const string RegistryPath = @"Software\AlienCommandCenter\KeyboardStudio";
    private const string RegistryValueName = "ActiveLighting";
    private const string BacklightActiveSecondsValueName = "BacklightActiveSeconds";
    public const int MaximumBacklightActiveSeconds = 86400;

    public ActiveLightingSnapshot? Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var json = key?.GetValue(RegistryValueName) as string;
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize<ActiveLightingSnapshot>(json);
            return IsValid(snapshot) ? snapshot : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            SecurityException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public int LoadBacklightActiveSeconds()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            var value = key?.GetValue(BacklightActiveSecondsValueName);
            return value is int seconds ? Math.Clamp(seconds, 0, MaximumBacklightActiveSeconds) : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            SecurityException or ArgumentException)
        {
            return 0;
        }
    }

    public bool SaveBacklightActiveSeconds(int seconds)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (key is null)
            {
                return false;
            }

            key.SetValue(
                BacklightActiveSecondsValueName,
                Math.Clamp(seconds, 0, MaximumBacklightActiveSeconds),
                RegistryValueKind.DWord);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            SecurityException or ArgumentException)
        {
            return false;
        }
    }

    public bool Save(IReadOnlyList<ZoneConfiguration> zones, int brightness, bool isBacklightOn)
    {
        var snapshot = new ActiveLightingSnapshot
        {
            Brightness = Math.Clamp(brightness, 0, 100),
            IsBacklightOn = isBacklightOn,
            Zones = zones.Select(zone => zone.Copy()).ToList()
        };

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            if (key is null)
            {
                return false;
            }

            key.SetValue(RegistryValueName, JsonSerializer.Serialize(snapshot), RegistryValueKind.String);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            SecurityException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValid(ActiveLightingSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.Brightness is < 0 or > 100 ||
            snapshot.Zones is null || snapshot.Zones.Count != KeyboardZones.Ordered.Count)
        {
            return false;
        }

        var zonesByName = new Dictionary<string, ZoneConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in snapshot.Zones)
        {
            if (zone is null || string.IsNullOrWhiteSpace(zone.ZoneName) ||
                !IsHexColor(zone.ColorHex) || !Enum.IsDefined(zone.Effect) || zone.Speed is < 1 or > 100 ||
                zone.WaveStepDelaySeconds is < 1 or > 10 || !zonesByName.TryAdd(zone.ZoneName, zone))
            {
                return false;
            }
        }

        return KeyboardZones.Ordered.All(zonesByName.ContainsKey);
    }

    private static bool IsHexColor(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var digits = value.Trim().TrimStart('#');
        return digits.Length == 6 && digits.All(Uri.IsHexDigit);
    }
}

public sealed class ActiveLightingSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public int Brightness { get; set; } = 100;
    public bool IsBacklightOn { get; set; } = true;
    public List<ZoneConfiguration> Zones { get; set; } = [];
}
