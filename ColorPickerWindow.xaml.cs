using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AlienCommandCenter;

public partial class ColorPickerWindow : Window
{
    private bool _controlsReady;
    private bool _updatingControls;

    public ColorPickerWindow(string initialColor)
    {
        InitializeComponent();
        _controlsReady = true;
        SetColor(TryParseHex(initialColor, out var color) ? color : Colors.MediumAquamarine);
    }

    public string SelectedColorHex { get; private set; } = "#69E0C1";

    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_controlsReady || _updatingControls)
        {
            return;
        }

        var color = Color.FromRgb(
            (byte)Math.Round(RedSlider.Value),
            (byte)Math.Round(GreenSlider.Value),
            (byte)Math.Round(BlueSlider.Value));
        SetColor(color);
    }

    private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_controlsReady || _updatingControls)
        {
            return;
        }

        if (!TryParseHex(HexInput.Text, out var color))
        {
            HexError.Visibility = Visibility.Visible;
            return;
        }

        HexError.Visibility = Visibility.Collapsed;
        _updatingControls = true;
        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        _updatingControls = false;
        UpdatePreview(color);
    }

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && TryParseHex(value, out var color))
        {
            SetColor(color);
        }
    }

    private void UseColor_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseHex(HexInput.Text, out var color))
        {
            HexError.Visibility = Visibility.Visible;
            return;
        }

        SelectedColorHex = FormatHex(color);
        DialogResult = true;
    }

    private void SetColor(Color color)
    {
        _updatingControls = true;
        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        HexInput.Text = FormatHex(color);
        _updatingControls = false;
        HexError.Visibility = Visibility.Collapsed;
        UpdatePreview(color);
    }

    private void UpdatePreview(Color color)
    {
        ColorPreview.Background = new SolidColorBrush(color);
        RedValue.Text = color.R.ToString(CultureInfo.InvariantCulture);
        GreenValue.Text = color.G.ToString(CultureInfo.InvariantCulture);
        BlueValue.Text = color.B.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParseHex(string? value, out Color color)
    {
        color = Colors.Transparent;
        if (value is null)
        {
            return false;
        }

        var digits = value.Trim();
        if (digits.StartsWith('#'))
        {
            digits = digits[1..];
        }

        if (digits.Length != 6 ||
            !byte.TryParse(digits.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(digits.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(digits.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = Color.FromRgb(red, green, blue);
        return true;
    }

    private static string FormatHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
