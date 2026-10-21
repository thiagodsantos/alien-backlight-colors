using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;
using AlienCommandCenter.Models;
using AlienCommandCenter.Services;

namespace AlienCommandCenter.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly PresetStore _presetStore;
    private readonly IKeyboardLightingService _lightingService;
    private readonly RelayCommand _loadPresetCommand;
    private readonly AsyncRelayCommand _applyPresetCommand;
    private readonly RelayCommand _renamePresetCommand;
    private readonly RelayCommand _deletePresetCommand;
    private LightingPreset? _selectedPreset;
    private string _presetName = "My lighting preset";
    private string _statusMessage = string.Empty;
    private bool _isStatusError;
    private int _brightness = 100;
    private bool _isBacklightOn = true;

    public MainViewModel(PresetStore presetStore, IKeyboardLightingService lightingService)
    {
        _presetStore = presetStore;
        _lightingService = lightingService;

        Zones = new ObservableCollection<ZoneViewModel>(
        [
            new ZoneViewModel(KeyboardZones.AlphaLeft),
            new ZoneViewModel(KeyboardZones.AlphaCenter),
            new ZoneViewModel(KeyboardZones.AlphaRight),
            new ZoneViewModel(KeyboardZones.NumericKeypad)
        ]);

        Presets = new ObservableCollection<LightingPreset>();
        foreach (var preset in CreateBuiltInPresets())
        {
            Presets.Add(preset);
        }

        var userPresets = _presetStore.Load(out var loadWarning);
        foreach (var preset in userPresets.OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase))
        {
            Presets.Add(preset);
        }

        var deviceStatus = _lightingService.GetStatus();
        DeviceHeadline = deviceStatus.DeviceName;
        DeviceDescription = deviceStatus.Message;
        DeviceStatusLabel = deviceStatus.IsConnected ? "DEVICE CONNECTED" : "PREVIEW MODE";
        IsDeviceConnected = deviceStatus.IsConnected;

        _loadPresetCommand = new RelayCommand(LoadSelectedPreset, () => SelectedPreset is not null);
        _applyPresetCommand = new AsyncRelayCommand(ApplySelectedPresetAsync, () => SelectedPreset is not null);
        _renamePresetCommand = new RelayCommand(RenameSelectedPreset, () => SelectedPreset is { IsBuiltIn: false });
        _deletePresetCommand = new RelayCommand(DeleteSelectedPreset, () => SelectedPreset is { IsBuiltIn: false });

        SavePresetCommand = new RelayCommand(SavePreset);
        LoadPresetCommand = _loadPresetCommand;
        ApplyPresetCommand = _applyPresetCommand;
        RenamePresetCommand = _renamePresetCommand;
        DeletePresetCommand = _deletePresetCommand;
        ApplyCurrentCommand = new AsyncRelayCommand(ApplyCurrentAsync);
        ResetDeviceCommand = new AsyncRelayCommand(ResetDeviceAsync);
        ToggleBacklightCommand = new AsyncRelayCommand(ToggleBacklightAsync);

        SelectedPreset = Presets.FirstOrDefault();
        SetStatus(loadWarning ?? deviceStatus.Message, isError: loadWarning is not null);
    }

    public ObservableCollection<ZoneViewModel> Zones { get; }
    public ObservableCollection<LightingPreset> Presets { get; }

    public string DeviceHeadline { get; }
    public string DeviceDescription { get; }
    public string DeviceStatusLabel { get; }
    public bool IsDeviceConnected { get; }

    public string PresetName
    {
        get => _presetName;
        set => SetProperty(ref _presetName, value);
    }

    public int Brightness
    {
        get => _brightness;
        set
        {
            if (SetProperty(ref _brightness, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(BrightnessLabel));
                OnPropertyChanged(nameof(EffectiveBrightness));
                OnPropertyChanged(nameof(BacklightToggleLabel));
            }
        }
    }

    public bool IsBacklightOn
    {
        get => _isBacklightOn;
        set
        {
            if (SetProperty(ref _isBacklightOn, value))
            {
                OnPropertyChanged(nameof(EffectiveBrightness));
                OnPropertyChanged(nameof(BacklightToggleLabel));
            }
        }
    }

    public int EffectiveBrightness => IsBacklightOn ? Brightness : 0;
    public string BrightnessLabel => $"{Brightness}%";
    public string BacklightToggleLabel => EffectiveBrightness > 0 ? "Turn backlight off" : "Turn backlight on";

    public LightingPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                _loadPresetCommand?.NotifyCanExecuteChanged();
                _applyPresetCommand?.NotifyCanExecuteChanged();
                _renamePresetCommand?.NotifyCanExecuteChanged();
                _deletePresetCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public Brush StatusBrush => _isStatusError ? Brushes.Salmon : Brushes.LightGray;

    public ICommand SavePresetCommand { get; }
    public ICommand LoadPresetCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand RenamePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand ApplyCurrentCommand { get; }
    public ICommand ResetDeviceCommand { get; }
    public ICommand ToggleBacklightCommand { get; }

    private void SavePreset()
    {
        if (Zones.Any(zone => !zone.IsInputValid))
        {
            SetStatus("Fix invalid zone colors before saving this preset.", isError: true);
            return;
        }

        var name = PresetName.Trim();
        if (name.Length == 0)
        {
            SetStatus("Enter a name for this preset.", isError: true);
            return;
        }

        if (Presets.Any(preset => preset.IsBuiltIn && string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("That name is reserved for a built-in preset.", isError: true);
            return;
        }

        var existing = Presets.FirstOrDefault(preset => !preset.IsBuiltIn &&
            string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
        var savedPreset = new LightingPreset
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Name = name,
            UpdatedAt = DateTimeOffset.Now,
            Brightness = Brightness,
            IsBacklightOn = IsBacklightOn,
            Zones = SnapshotZones()
        };

        if (existing is not null)
        {
            Presets.Remove(existing);
        }

        Presets.Add(savedPreset);
        SelectedPreset = savedPreset;

        try
        {
            PersistUserPresets();
            SetStatus($"Preset “{name}” saved on this PC.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not save preset: {exception.Message}", isError: true);
        }
    }

    private void LoadSelectedPreset()
    {
        if (SelectedPreset is not { } preset)
        {
            return;
        }

        LoadZones(preset.Zones);
        Brightness = preset.Brightness;
        IsBacklightOn = preset.IsBacklightOn;
        SetStatus($"“{preset.Name}” loaded into the editor. Apply it to send it to the keyboard.", isError: false);
    }

    private async Task ApplySelectedPresetAsync()
    {
        if (SelectedPreset is not { } preset)
        {
            return;
        }

        try
        {
            var result = await _lightingService.ApplyAsync(
                preset.Zones.Select(zone => zone.Copy()).ToList(),
                preset.IsBacklightOn ? preset.Brightness : 0);
            if (result.Succeeded)
            {
                LoadZones(preset.Zones);
                Brightness = preset.Brightness;
                IsBacklightOn = preset.IsBacklightOn;
            }

            SetStatus(result.Message, isError: !result.Succeeded);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not apply preset: {exception.Message}", isError: true);
        }
    }

    private void RenameSelectedPreset()
    {
        if (SelectedPreset is not { IsBuiltIn: false } preset)
        {
            return;
        }

        var name = PresetName.Trim();
        if (name.Length == 0)
        {
            SetStatus("Enter a new preset name.", isError: true);
            return;
        }

        if (Presets.Any(candidate => candidate.Id != preset.Id &&
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("A preset with that name already exists.", isError: true);
            return;
        }

        var renamedPreset = new LightingPreset
        {
            Id = preset.Id,
            Name = name,
            UpdatedAt = DateTimeOffset.Now,
            Brightness = preset.Brightness,
            IsBacklightOn = preset.IsBacklightOn,
            Zones = preset.Zones.Select(zone => zone.Copy()).ToList()
        };
        var index = Presets.IndexOf(preset);
        Presets[index] = renamedPreset;
        SelectedPreset = renamedPreset;

        try
        {
            PersistUserPresets();
            SetStatus($"Preset renamed to “{name}”.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not save the renamed preset: {exception.Message}", isError: true);
        }
    }

    private void DeleteSelectedPreset()
    {
        if (SelectedPreset is not { IsBuiltIn: false } preset)
        {
            return;
        }

        Presets.Remove(preset);
        SelectedPreset = Presets.FirstOrDefault();

        try
        {
            PersistUserPresets();
            SetStatus($"Preset “{preset.Name}” deleted.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not update saved presets: {exception.Message}", isError: true);
        }
    }

    private async Task ApplyCurrentAsync()
    {
        if (Zones.Any(zone => !zone.IsInputValid))
        {
            SetStatus("Fix invalid zone colors before applying lighting.", isError: true);
            return;
        }

        try
        {
            var result = await _lightingService.ApplyAsync(SnapshotZones(), EffectiveBrightness);
            SetStatus(result.Message, isError: !result.Succeeded);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not apply lighting: {exception.Message}", isError: true);
        }
    }

    private async Task ToggleBacklightAsync()
    {
        var turnOn = EffectiveBrightness == 0;
        var targetBrightness = turnOn ? (Brightness == 0 ? 100 : Brightness) : 0;
        var staticZones = SnapshotZones();
        foreach (var zone in staticZones)
        {
            zone.Effect = LightingEffect.Static;
        }

        try
        {
            var result = await _lightingService.ApplyAsync(staticZones, targetBrightness);
            if (result.Succeeded)
            {
                if (turnOn && Brightness == 0)
                {
                    Brightness = 100;
                }

                IsBacklightOn = turnOn;
                SetStatus(
                    turnOn
                        ? $"Backlight on at {targetBrightness}% brightness."
                        : "Backlight off; zone colors remain in the editor.",
                    isError: false);
            }
            else
            {
                SetStatus(result.Message, isError: true);
            }
        }
        catch (Exception exception)
        {
            SetStatus($"Could not change backlight power: {exception.Message}", isError: true);
        }
    }

    private async Task ResetDeviceAsync()
    {
        try
        {
            var result = await _lightingService.ResetAsync();
            if (result.Succeeded)
            {
                foreach (var zone in Zones)
                {
                    zone.Load(new ZoneConfiguration
                    {
                        ZoneName = zone.ZoneName,
                        ColorHex = "#FF0000",
                        SecondaryColorHex = "#000000",
                        Effect = LightingEffect.Static,
                        Speed = 50
                    });
                }

                Brightness = 100;
                IsBacklightOn = true;
            }

            SetStatus(result.Message, isError: !result.Succeeded);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not reset keyboard lighting: {exception.Message}", isError: true);
        }
    }

    private List<ZoneConfiguration> SnapshotZones() => Zones.Select(zone => zone.ToConfiguration()).ToList();

    private void LoadZones(IEnumerable<ZoneConfiguration> configurations)
    {
        foreach (var zone in Zones)
        {
            var configuration = configurations.FirstOrDefault(candidate =>
                string.Equals(candidate.ZoneName, zone.ZoneName, StringComparison.OrdinalIgnoreCase));
            if (configuration is not null)
            {
                zone.Load(configuration);
            }
        }
    }

    private void PersistUserPresets() => _presetStore.Save(Presets);

    private void SetStatus(string message, bool isError)
    {
        _isStatusError = isError;
        StatusMessage = message;
        OnPropertyChanged(nameof(StatusBrush));
    }

    private static IEnumerable<LightingPreset> CreateBuiltInPresets()
    {
        yield return CreateBuiltIn(
            "Arctic Zones",
            ["#2BD9C5", "#B8FFF1", "#2BD9C5", "#0D8D78"],
            LightingEffect.Static,
            speed: 50,
            secondary: "#FFFFFF");
        yield return CreateBuiltIn(
            "Solar Zones",
            ["#FF542E", "#FFB23F", "#FF542E", "#FFD36A"],
            LightingEffect.Static,
            speed: 50,
            secondary: "#FFD36A");
    }

    private static LightingPreset CreateBuiltIn(
        string name,
        IReadOnlyList<string> colors,
        LightingEffect effect,
        int speed,
        string secondary) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsBuiltIn = true,
        Zones = new List<ZoneConfiguration>
        {
            new() { ZoneName = KeyboardZones.AlphaLeft, ColorHex = colors[0], Effect = effect, Speed = speed, SecondaryColorHex = secondary },
            new() { ZoneName = KeyboardZones.AlphaCenter, ColorHex = colors[1], Effect = effect, Speed = speed, SecondaryColorHex = secondary },
            new() { ZoneName = KeyboardZones.AlphaRight, ColorHex = colors[2], Effect = effect, Speed = speed, SecondaryColorHex = secondary },
            new() { ZoneName = KeyboardZones.NumericKeypad, ColorHex = colors[3], Effect = effect, Speed = speed, SecondaryColorHex = secondary }
        }
    };
}
