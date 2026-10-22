using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlienCommandCenter.Models;

namespace AlienCommandCenter.Services;

public sealed class PresetStore
{
    private static readonly string[] ZoneNames = KeyboardZones.Ordered.ToArray();

    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public PresetStore()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AlienCommandCenter",
            "presets.json");
    }

    public List<LightingPreset> Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var document = JsonSerializer.Deserialize<PresetDocument>(File.ReadAllText(_filePath), _jsonOptions);
            if (document is null || document.SchemaVersion != 1 || document.Presets is null)
            {
                warning = "The preset file has an unsupported format. Save a preset to replace it with the current format.";
                return [];
            }

            var validPresets = new List<LightingPreset>();
            foreach (var preset in document.Presets)
            {
                if (TryMigrateLegacyThreeZonePreset(preset))
                {
                    warning = "Older three-zone presets were expanded across the three alphanumeric zones and numeric keypad.";
                }

                if (!IsValid(preset))
                {
                    warning = "Some saved presets were invalid and have been skipped.";
                    continue;
                }

                preset!.Zones = ZoneNames
                    .Select(zoneName => preset.Zones.First(zone => string.Equals(zone.ZoneName, zoneName, StringComparison.OrdinalIgnoreCase)))
                    .Select(zone =>
                    {
                        var copy = zone.Copy();
                        copy.ZoneName = ZoneNames.First(name => string.Equals(name, zone.ZoneName, StringComparison.OrdinalIgnoreCase));
                        copy.ColorHex = NormalizeColor(copy.ColorHex);
                        copy.Speed = Math.Clamp(copy.Speed, 1, 100);
                        copy.WaveStepDelaySeconds = Math.Clamp(copy.WaveStepDelaySeconds, 1, 10);
                        return copy;
                    })
                    .ToList();
                preset.Brightness = Math.Clamp(preset.Brightness, 0, 100);
                preset.IsBuiltIn = false;
                validPresets.Add(preset);
            }

            return validPresets;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = "Saved presets could not be read. The original file has been left untouched until you save a preset.";
            return [];
        }
    }

    public void Save(IEnumerable<LightingPreset> presets)
    {
        var document = new PresetDocument
        {
            SchemaVersion = 1,
            Presets = presets
                .Where(preset => !preset.IsBuiltIn)
                .Select(preset => new LightingPreset
                {
                    SchemaVersion = 1,
                    Id = preset.Id,
                    Name = preset.Name.Trim(),
                    UpdatedAt = preset.UpdatedAt,
                    Brightness = Math.Clamp(preset.Brightness, 0, 100),
                    IsBacklightOn = preset.IsBacklightOn,
                    Zones = preset.Zones.Select(zone => zone.Copy()).ToList()
                })
                .ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, _jsonOptions));
        File.Move(temporaryPath, _filePath, overwrite: true);
    }

    private static bool TryMigrateLegacyThreeZonePreset(LightingPreset? preset)
    {
        if (preset?.Zones is not { Count: 3 })
        {
            return false;
        }

        var left = preset.Zones.FirstOrDefault(zone => string.Equals(zone.ZoneName, "Left", StringComparison.OrdinalIgnoreCase));
        var center = preset.Zones.FirstOrDefault(zone => string.Equals(zone.ZoneName, "Center", StringComparison.OrdinalIgnoreCase));
        var right = preset.Zones.FirstOrDefault(zone => string.Equals(zone.ZoneName, "Right", StringComparison.OrdinalIgnoreCase));
        if (!IsValidZone(left) || !IsValidZone(center) || !IsValidZone(right))
        {
            return false;
        }

        preset.Zones =
        [
            CopyToZone(left!, KeyboardZones.AlphaLeft),
            CopyToZone(center!, KeyboardZones.AlphaCenter),
            CopyToZone(right!, KeyboardZones.AlphaRight),
            CopyToZone(right!, KeyboardZones.NumericKeypad)
        ];
        return true;
    }

    private static ZoneConfiguration CopyToZone(ZoneConfiguration source, string zoneName)
    {
        var copy = source.Copy();
        copy.ZoneName = zoneName;
        return copy;
    }

    private static bool IsValidZone(ZoneConfiguration? zone) =>
        zone is not null && IsHexColor(zone.ColorHex) && Enum.IsDefined(zone.Effect);

    private static bool IsValid(LightingPreset? preset)
    {
        if (preset is null || preset.SchemaVersion != 1 || preset.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(preset.Name) || preset.Zones is null || preset.Zones.Count != ZoneNames.Length)
        {
            return false;
        }

        foreach (var zoneName in ZoneNames)
        {
            var zone = preset.Zones.FirstOrDefault(candidate =>
                candidate is not null && string.Equals(candidate.ZoneName, zoneName, StringComparison.OrdinalIgnoreCase));
            if (!IsValidZone(zone))
            {
                return false;
            }
        }

        return true;
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

    private static string NormalizeColor(string value)
    {
        var digits = value.Trim().TrimStart('#');
        return $"#{digits.ToUpperInvariant()}";
    }

    private sealed class PresetDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<LightingPreset> Presets { get; set; } = [];
    }
}
