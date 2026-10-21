namespace AlienCommandCenter.Models;

public static class KeyboardZones
{
    public const string AlphaLeft = "Left (alpha)";
    public const string AlphaCenter = "Center (alpha)";
    public const string AlphaRight = "Right (alpha)";
    public const string NumericKeypad = "Numeric keypad";

    public static IReadOnlyList<string> Ordered { get; } = Array.AsReadOnly(
    [
        AlphaLeft,
        AlphaCenter,
        AlphaRight,
        NumericKeypad
    ]);
}
