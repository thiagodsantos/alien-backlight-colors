using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using AlienCommandCenter.Models;

namespace AlienCommandCenter.ViewModels;

public sealed class ZoneViewModel : ObservableObject
{
    private string _colorHex;
    private string _validColorHex;

    public ZoneViewModel(string zoneName)
    {
        ZoneName = zoneName;
        _validColorHex = "#69E0C1";
        _colorHex = _validColorHex;
    }

    public string ZoneName { get; }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (!SetProperty(ref _colorHex, value))
            {
                return;
            }

            if (IsHexColor(value))
            {
                _validColorHex = NormalizeColor(value, _validColorHex);
            }

            OnPropertyChanged(nameof(ColorBrush));
            OnPropertyChanged(nameof(ColorError));
            OnPropertyChanged(nameof(IsInputValid));
        }
    }

    public Brush ColorBrush => CreateBrush(_validColorHex);
    public string ColorError => IsHexColor(ColorHex) ? string.Empty : "Enter a 6-digit hex color.";
    public bool IsInputValid => IsHexColor(ColorHex);

    public ZoneConfiguration ToConfiguration(LightingEffect effect, int speed, int waveStepDelaySeconds) => new()
    {
        ZoneName = ZoneName,
        ColorHex = _validColorHex,
        Effect = effect,
        Speed = speed,
        WaveStepDelaySeconds = waveStepDelaySeconds
    };

    public void Load(ZoneConfiguration configuration)
    {
        ColorHex = NormalizeColor(configuration.ColorHex, "#69E0C1");
    }

    private static bool IsHexColor(string? value)
    {
        if (value is null)
        {
            return false;
        }

        var digits = value.Trim();
        if (digits.StartsWith('#'))
        {
            digits = digits[1..];
        }

        return digits.Length == 6 && digits.All(Uri.IsHexDigit);
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (!IsHexColor(value))
        {
            return fallback;
        }

        var digits = value!.Trim();
        if (digits.StartsWith('#'))
        {
            digits = digits[1..];
        }

        return $"#{digits.ToUpperInvariant()}";
    }

    private static Brush CreateBrush(string colorHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
