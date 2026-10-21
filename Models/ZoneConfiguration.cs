namespace AlienCommandCenter.Models;

public sealed class ZoneConfiguration
{
    public string ZoneName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#69E0C1";
    public LightingEffect Effect { get; set; } = LightingEffect.Static;
    public int Speed { get; set; } = 50;
    public string SecondaryColorHex { get; set; } = "#FFFFFF";

    public ZoneConfiguration Copy() => new()
    {
        ZoneName = ZoneName,
        ColorHex = ColorHex,
        Effect = Effect,
        Speed = Speed,
        SecondaryColorHex = SecondaryColorHex
    };
}
