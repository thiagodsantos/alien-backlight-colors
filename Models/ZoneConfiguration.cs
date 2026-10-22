namespace AlienCommandCenter.Models;

public sealed class ZoneConfiguration
{
    public string ZoneName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#59D9F5";
    public LightingEffect Effect { get; set; } = LightingEffect.Static;
    public int Speed { get; set; } = 50;

    public ZoneConfiguration Copy() => new()
    {
        ZoneName = ZoneName,
        ColorHex = ColorHex,
        Effect = Effect,
        Speed = Speed
    };
}
