using System.Text.Json.Serialization;

namespace AlienCommandCenter.Models;

public sealed class LightingPreset
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "My preset";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public int Brightness { get; set; } = 100;
    public bool IsBacklightOn { get; set; } = true;
    public List<ZoneConfiguration> Zones { get; set; } = [];

    [JsonIgnore]
    public bool IsBuiltIn { get; set; }
}
