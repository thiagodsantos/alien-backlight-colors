using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using AlienCommandCenter.Models;

namespace AlienCommandCenter.ViewModels;

public sealed class ZoneViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> AvailableEffects = Enum.GetNames<LightingEffect>();

    private string _colorHex;
    private string _validColorHex;
    private string _selectedEffect;
    private int _speed;

    public ZoneViewModel(string zoneName, ZoneConfiguration? configuration = null)
    {
        ZoneName = zoneName;
        configuration ??= new ZoneConfiguration { ZoneName = zoneName };
        _validColorHex = NormalizeColor(configuration.ColorHex, "#69E0C1");
        _colorHex = _validColorHex;
        _selectedEffect = Enum.IsDefined(configuration.Effect) ? configuration.Effect.ToString() : LightingEffect.Static.ToString();
        _speed = Math.Clamp(configuration.Speed, 1, 100);
    }

    public string ZoneName { get; }
    public IReadOnlyList<string> EffectOptions => AvailableEffects;
    public string SpeedLabel => $"{Speed}%";

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

    public string SelectedEffect
    {
        get => _selectedEffect;
        set => SetProperty(ref _selectedEffect, value);
    }

    public int Speed
    {
        get => _speed;
        set
        {
            if (SetProperty(ref _speed, Math.Clamp(value, 1, 100)))
            {
                OnPropertyChanged(nameof(SpeedLabel));
            }
        }
    }

    public Brush ColorBrush => CreateBrush(_validColorHex);
    public string ColorError => IsHexColor(ColorHex) ? string.Empty : "Enter a 6-digit hex color.";
    public bool IsInputValid => IsHexColor(ColorHex);

    public ZoneConfiguration ToConfiguration() => new()
    {
        ZoneName = ZoneName,
        ColorHex = _validColorHex,
        Effect = Enum.TryParse<LightingEffect>(SelectedEffect, out var effect) ? effect : LightingEffect.Static,
        Speed = Speed
    };

    public void Load(ZoneConfiguration configuration)
    {
        ColorHex = NormalizeColor(configuration.ColorHex, "#69E0C1");
        SelectedEffect = Enum.IsDefined(configuration.Effect) ? configuration.Effect.ToString() : LightingEffect.Static.ToString();
        Speed = configuration.Speed;
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
