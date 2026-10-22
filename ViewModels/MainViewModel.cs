using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using AlienCommandCenter.Models;
using AlienCommandCenter.Services;

namespace AlienCommandCenter.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly PresetStore _presetStore;
    private readonly ActiveLightingStore _activeLightingStore;
    private readonly IKeyboardLightingService _lightingService;
    private readonly RelayCommand _loadPresetCommand;
    private readonly AsyncRelayCommand _applyPresetCommand;
    private readonly RelayCommand _renamePresetCommand;
    private readonly RelayCommand _deletePresetCommand;
    private static readonly IReadOnlyList<string> AvailableEffects = Enum.GetNames<LightingEffect>();
    private static readonly byte[] MatrixGreenPalette = [8, 16, 28, 45, 70, 110, 170, 255];
    private LightingPreset? _selectedPreset;
    private string _presetName = "My lighting preset";
    private string _selectedEffect = nameof(LightingEffect.Static);
    private int _effectSpeed = 50;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;
    private int _brightness = 100;
    private int _backlightActiveSeconds;
    private bool _isBacklightOn = true;
    private CancellationTokenSource? _animationCancellation;
    private Task? _animationTask;

    public MainViewModel(
        PresetStore presetStore,
        ActiveLightingStore activeLightingStore,
        IKeyboardLightingService lightingService)
    {
        _presetStore = presetStore;
        _activeLightingStore = activeLightingStore;
        _lightingService = lightingService;
        _backlightActiveSeconds = _activeLightingStore.LoadBacklightActiveSeconds();
        _ = _lightingService.ConfigureBacklightActiveTimeoutAsync(_backlightActiveSeconds);

        Zones = new ObservableCollection<ZoneViewModel>(
        [
            new ZoneViewModel(KeyboardZones.AlphaLeft),
            new ZoneViewModel(KeyboardZones.AlphaCenter),
            new ZoneViewModel(KeyboardZones.AlphaRight),
            new ZoneViewModel(KeyboardZones.NumericKeypad)
        ]);

        var activeLighting = _activeLightingStore.Load();
        if (activeLighting is not null)
        {
            LoadZones(activeLighting.Zones);
            Brightness = activeLighting.Brightness;
            IsBacklightOn = activeLighting.IsBacklightOn;
        }

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
        ApplyCurrentCommand = new AsyncRelayCommand(() => ApplyCurrentAsync(forceBacklightOn: true));
        ResetDeviceCommand = new AsyncRelayCommand(ResetDeviceAsync);
        ToggleBacklightCommand = new AsyncRelayCommand(ToggleBacklightAsync);

        SelectedPreset = Presets.FirstOrDefault();
        SetStatus(loadWarning ?? deviceStatus.Message, isError: loadWarning is not null);

        if (deviceStatus.IsConnected && (activeLighting is not null || IsBacklightOn))
        {
            _ = ApplyCurrentAsync(forceBacklightOn: false);
        }
    }

    public ObservableCollection<ZoneViewModel> Zones { get; }
    public ObservableCollection<LightingPreset> Presets { get; }
    public IReadOnlyList<string> EffectOptions => AvailableEffects;

    public string DeviceHeadline { get; }
    public string DeviceDescription { get; }
    public string DeviceStatusLabel { get; }
    public bool IsDeviceConnected { get; }

    public string PresetName
    {
        get => _presetName;
        set => SetProperty(ref _presetName, value);
    }

    public string SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            if (Enum.TryParse<LightingEffect>(value, out var effect) && Enum.IsDefined(effect))
            {
                SetProperty(ref _selectedEffect, effect.ToString());
            }
        }
    }


    public int EffectSpeed
    {
        get => _effectSpeed;
        set
        {
            if (SetProperty(ref _effectSpeed, Math.Clamp(value, 1, 100)))
            {
                OnPropertyChanged(nameof(EffectSpeedLabel));
            }
        }
    }

    public string EffectSpeedLabel => $"{EffectSpeed}%";

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

    public int BacklightActiveSeconds
    {
        get => _backlightActiveSeconds;
        set
        {
            var seconds = Math.Clamp(value, 0, ActiveLightingStore.MaximumBacklightActiveSeconds);
            if (SetProperty(ref _backlightActiveSeconds, seconds))
            {
                _ = _activeLightingStore.SaveBacklightActiveSeconds(seconds);
                _ = _lightingService.ConfigureBacklightActiveTimeoutAsync(seconds);
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
            var targetBrightness = preset.Brightness == 0 ? 100 : preset.Brightness;
            var zones = NormalizeSharedEffect(preset.Zones);
            var result = await ApplyZonesWithEffectAsync(zones, targetBrightness);
            if (result.Succeeded)
            {
                LoadZones(zones);
                Brightness = targetBrightness;
                IsBacklightOn = true;
                StoreAppliedLighting(zones, Brightness, IsBacklightOn);
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

    private async Task ApplyCurrentAsync(bool forceBacklightOn)
    {
        if (Zones.Any(zone => !zone.IsInputValid))
        {
            SetStatus("Fix invalid zone colors before applying lighting.", isError: true);
            return;
        }

        try
        {
            var zones = SnapshotZones();
            var targetBrightness = forceBacklightOn
                ? (Brightness == 0 ? 100 : Brightness)
                : EffectiveBrightness;
            var result = await ApplyZonesWithEffectAsync(zones, targetBrightness);
            if (result.Succeeded)
            {
                if (forceBacklightOn)
                {
                    Brightness = targetBrightness;
                    IsBacklightOn = true;
                }

                StoreAppliedLighting(zones, Brightness, IsBacklightOn);
            }

            SetStatus(result.Message, isError: !result.Succeeded);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not apply lighting: {exception.Message}", isError: true);
        }
    }

    private async Task<HardwareResult> ApplyZonesWithEffectAsync(
        IReadOnlyList<ZoneConfiguration> zones,
        int brightnessPercent)
    {
        await StopAnimationAsync();

        var normalizedZones = NormalizeSharedEffect(zones);
        var sharedConfiguration = normalizedZones.FirstOrDefault();
        var effect = sharedConfiguration?.Effect ?? LightingEffect.Static;
        if (effect == LightingEffect.Static || brightnessPercent == 0)
        {
            return await ApplyStaticZonesAsync(normalizedZones, brightnessPercent);
        }

        var speed = Math.Clamp(sharedConfiguration?.Speed ?? 50, 1, 100);
        var staticZones = ToStaticZones(normalizedZones);
        var initialFrame = CreateEffectFrame(staticZones, effect, speed, 0, brightnessPercent);
        var result = await _lightingService.ApplyAsync(initialFrame.Zones, initialFrame.BrightnessPercent);
        if (!result.Succeeded)
        {
            return result;
        }

        var cancellation = new CancellationTokenSource();
        _animationCancellation = cancellation;
        _animationTask = RunEffectAsync(
            staticZones,
            effect,
            speed,
            brightnessPercent,
            cancellation.Token);
        return new HardwareResult(true, $"Running the {effect} effect using software-generated static frames.");
    }

    private async Task<HardwareResult> ApplyStaticZonesAsync(
        IReadOnlyList<ZoneConfiguration> zones,
        int brightnessPercent)
    {
        await StopAnimationAsync();
        return await _lightingService.ApplyAsync(ToStaticZones(zones), brightnessPercent);
    }

    private async Task StopAnimationAsync()
    {
        var cancellation = _animationCancellation;
        if (cancellation is null)
        {
            return;
        }

        _animationCancellation = null;
        cancellation.Cancel();
        var animationTask = _animationTask;
        _animationTask = null;
        if (animationTask is not null)
        {
            try
            {
                await animationTask;
            }
            finally
            {
                cancellation.Dispose();
            }
        }
        else
        {
            cancellation.Dispose();
        }
    }

    public void StopAnimation()
    {
        var cancellation = _animationCancellation;
        _animationCancellation = null;
        cancellation?.Cancel();
        if (cancellation is not null && _animationTask is { } animationTask)
        {
            _ = animationTask.ContinueWith(
                _ => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        _animationTask = null;
    }

    private async Task RunEffectAsync(
        IReadOnlyList<ZoneConfiguration> baseZones,
        LightingEffect effect,
        int speed,
        int brightnessPercent,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var frameInterval = TimeSpan.FromMilliseconds(Math.Clamp(230 - speed * 1.5, 80, 220));
        try
        {
            while (true)
            {
                await Task.Delay(frameInterval, cancellationToken);
                var frame = CreateEffectFrame(
                    baseZones,
                    effect,
                    speed,
                    stopwatch.Elapsed.TotalSeconds,
                    brightnessPercent);
                var result = await _lightingService.ApplyAnimationFrameAsync(frame.Zones, frame.BrightnessPercent);
                if (!result.Succeeded)
                {
                    SetStatus(result.Message, isError: true);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetStatus($"The {effect} effect stopped: {exception.GetBaseException().Message}", isError: true);
        }
    }

    private static (List<ZoneConfiguration> Zones, int BrightnessPercent) CreateEffectFrame(
        IReadOnlyList<ZoneConfiguration> baseZones,
        LightingEffect effect,
        int speed,
        double elapsedSeconds,
        int brightnessPercent)
    {
        var frameZones = ToStaticZones(baseZones);
        switch (effect)
        {
            case LightingEffect.Flashing:
            {
                var cycleSeconds = 2.0 - (speed * 0.018);
                var isOn = (elapsedSeconds % cycleSeconds) < (cycleSeconds / 2);
                if (!isOn)
                {
                    foreach (var zone in frameZones)
                    {
                        zone.ColorHex = ScaleColor(zone.ColorHex, 0);
                    }
                }

                return (frameZones, brightnessPercent);
            }
            case LightingEffect.Breathing:
            {
                var cycleSeconds = 6.0 - speed * 0.05;
                var breath = (Math.Sin((elapsedSeconds / cycleSeconds * 2 * Math.PI) - (Math.PI / 2)) + 1) / 2;
                var brightnessScale = 0.12 + (0.88 * breath);
                var frameBrightness = Math.Clamp((int)Math.Round(brightnessPercent * brightnessScale), 1, 100);
                return (frameZones, frameBrightness);
            }
            case LightingEffect.ColorCycle:
            {
                var degreesPerSecond = 18 + (speed * 1.5);
                for (var index = 0; index < frameZones.Count; index++)
                {
                    frameZones[index].ColorHex = CreateRainbowColor(elapsedSeconds * degreesPerSecond + (index * 90));
                }

                return (frameZones, brightnessPercent);
            }
            case LightingEffect.Matrix:
            {
                var cycleSeconds = 5.0 - (speed * 0.04);
                for (var index = 0; index < frameZones.Count; index++)
                {
                    var phase = ((elapsedSeconds / cycleSeconds) + (index * 0.25)) % 1;
                    var pulse = (Math.Sin((phase * 2 * Math.PI) - (Math.PI / 2)) + 1) / 2;
                    var paletteIndex = Math.Clamp(
                        (int)Math.Round(Math.Pow(pulse, 2) * (MatrixGreenPalette.Length - 1)),
                        0,
                        MatrixGreenPalette.Length - 1);
                    frameZones[index].ColorHex = $"#00{MatrixGreenPalette[paletteIndex]:X2}00";
                }

                return (frameZones, brightnessPercent);
            }
            case LightingEffect.PingPong:
            {
                var cycleSeconds = 6.0 - (speed * 0.05);
                var cyclePosition = (elapsedSeconds % cycleSeconds) / cycleSeconds;
                var travel = cyclePosition <= 0.5
                    ? cyclePosition * 2 * (frameZones.Count - 1)
                    : (1 - cyclePosition) * 2 * (frameZones.Count - 1);
                var lowerZone = (int)Math.Floor(travel);
                var upperZone = Math.Min(lowerZone + 1, frameZones.Count - 1);
                var progress = travel - lowerZone;

                for (var index = 0; index < frameZones.Count; index++)
                {
                    var zoneBrightness = lowerZone == upperZone
                        ? (index == lowerZone ? 1 : 0)
                        : index == lowerZone
                            ? 1 - progress
                            : index == upperZone ? progress : 0;
                    frameZones[index].ColorHex = ScaleColor("#FFFFFF", zoneBrightness);
                }

                return (frameZones, brightnessPercent);
            }
            case LightingEffect.Tunnel:
            {
                var cycleSeconds = 6.0 - (speed * 0.05);
                var cyclePosition = (elapsedSeconds % cycleSeconds) / cycleSeconds;
                double centerBrightness;
                double cornerBrightness;
                if (cyclePosition < 1.0 / 3)
                {
                    var progress = EaseInOut(cyclePosition * 3);
                    centerBrightness = progress;
                    cornerBrightness = 0;
                }
                else if (cyclePosition < 2.0 / 3)
                {
                    var progress = EaseInOut((cyclePosition - (1.0 / 3)) * 3);
                    centerBrightness = 1 - progress;
                    cornerBrightness = progress;
                }
                else
                {
                    var progress = EaseInOut((cyclePosition - (2.0 / 3)) * 3);
                    centerBrightness = 0;
                    cornerBrightness = 1 - progress;
                }

                for (var index = 0; index < frameZones.Count; index++)
                {
                    var zoneBrightness = index is 1 or 2 ? centerBrightness : cornerBrightness;
                    frameZones[index].ColorHex = ScaleColor("#FFFFFF", zoneBrightness);
                }

                return (frameZones, brightnessPercent);
            }
            case LightingEffect.Wave:
            {
                var stepDuration = 2.0 - (speed * 0.0165);
                var step = elapsedSeconds / stepDuration;
                var currentZone = (int)Math.Floor(step) % frameZones.Count;
                var nextZone = (currentZone + 1) % frameZones.Count;
                var progress = step - Math.Floor(step);
                progress = EaseInOut(progress);
                for (var index = 0; index < frameZones.Count; index++)
                {
                    var zoneBrightness = index == currentZone
                        ? 1 - progress
                        : index == nextZone ? progress : 0;
                    frameZones[index].ColorHex = ScaleColor(frameZones[index].ColorHex, zoneBrightness);
                }

                return (frameZones, brightnessPercent);
            }
            default:
                return (frameZones, brightnessPercent);
        }
    }

    private static double EaseInOut(double value)
    {
        var progress = Math.Clamp(value, 0, 1);
        return progress * progress * (3 - (2 * progress));
    }

    private static List<ZoneConfiguration> ToStaticZones(IEnumerable<ZoneConfiguration> zones) => zones
        .Select(zone =>
        {
            var copy = zone.Copy();
            copy.Effect = LightingEffect.Static;
            return copy;
        })
        .ToList();

    private static string CreateRainbowColor(double hueDegrees)
    {
        var hue = ((hueDegrees % 360) + 360) % 360;
        var sector = (int)(hue / 60);
        var fraction = (hue / 60) - sector;
        var secondary = 1 - Math.Abs((2 * fraction) - 1);
        var (red, green, blue) = sector switch
        {
            0 => (1.0, secondary, 0.0),
            1 => (secondary, 1.0, 0.0),
            2 => (0.0, 1.0, secondary),
            3 => (0.0, secondary, 1.0),
            4 => (secondary, 0.0, 1.0),
            _ => (1.0, 0.0, secondary)
        };
        return $"#{(int)Math.Round(red * 255):X2}{(int)Math.Round(green * 255):X2}{(int)Math.Round(blue * 255):X2}";
    }

    private static string ScaleColor(string colorHex, double scale)
    {
        var digits = colorHex.Trim().TrimStart('#');
        if (digits.Length != 6)
        {
            return colorHex;
        }

        var red = (int)Math.Round(Convert.ToByte(digits[..2], 16) * scale);
        var green = (int)Math.Round(Convert.ToByte(digits.Substring(2, 2), 16) * scale);
        var blue = (int)Math.Round(Convert.ToByte(digits.Substring(4, 2), 16) * scale);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private async Task ToggleBacklightAsync()
    {
        var turnOn = EffectiveBrightness == 0;
        var targetBrightness = turnOn ? (Brightness == 0 ? 100 : Brightness) : 0;
        var zones = SnapshotZones();

        try
        {
            var result = turnOn
                ? await ApplyZonesWithEffectAsync(zones, targetBrightness)
                : await ApplyStaticZonesAsync(zones, targetBrightness);
            if (result.Succeeded)
            {
                if (turnOn && Brightness == 0)
                {
                    Brightness = 100;
                }

                IsBacklightOn = turnOn;
                StoreAppliedLighting(SnapshotZones(), Brightness, IsBacklightOn);
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
            await StopAnimationAsync();
            var result = await _lightingService.ResetAsync();
            if (result.Succeeded)
            {
                foreach (var zone in Zones)
                {
                    zone.Load(new ZoneConfiguration
                    {
                        ZoneName = zone.ZoneName,
                        ColorHex = "#FF0000",
                        Effect = LightingEffect.Static,
                        Speed = 50
                    });
                }

                Brightness = 100;
                IsBacklightOn = true;
                SelectedEffect = nameof(LightingEffect.Static);
                EffectSpeed = 50;
                StoreAppliedLighting(SnapshotZones(), Brightness, IsBacklightOn);
            }

            SetStatus(result.Message, isError: !result.Succeeded);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not reset keyboard lighting: {exception.Message}", isError: true);
        }
    }

    private void StoreAppliedLighting(IReadOnlyList<ZoneConfiguration> zones, int brightness, bool isBacklightOn)
    {
        _ = _activeLightingStore.Save(zones, brightness, isBacklightOn);
    }

    private List<ZoneConfiguration> SnapshotZones() => Zones
        .Select(zone => zone.ToConfiguration(SelectedLightingEffect, EffectSpeed))
        .ToList();

    private void LoadZones(IEnumerable<ZoneConfiguration> configurations)
    {
        var configurationList = configurations.ToList();
        var sharedConfiguration = configurationList.FirstOrDefault(configuration =>
            string.Equals(configuration.ZoneName, KeyboardZones.AlphaLeft, StringComparison.OrdinalIgnoreCase))
            ?? configurationList.FirstOrDefault();
        if (sharedConfiguration is not null)
        {
            SelectedEffect = Enum.IsDefined(sharedConfiguration.Effect)
                ? sharedConfiguration.Effect.ToString()
                : nameof(LightingEffect.Static);
            EffectSpeed = sharedConfiguration.Speed;
        }

        foreach (var zone in Zones)
        {
            var configuration = configurationList.FirstOrDefault(candidate =>
                string.Equals(candidate.ZoneName, zone.ZoneName, StringComparison.OrdinalIgnoreCase));
            if (configuration is not null)
            {
                zone.Load(configuration);
            }
        }
    }

    private static List<ZoneConfiguration> NormalizeSharedEffect(IEnumerable<ZoneConfiguration> configurations)
    {
        var configurationList = configurations.ToList();
        var sharedConfiguration = configurationList.FirstOrDefault(configuration =>
            string.Equals(configuration.ZoneName, KeyboardZones.AlphaLeft, StringComparison.OrdinalIgnoreCase))
            ?? configurationList.FirstOrDefault();
        var effect = sharedConfiguration is not null && Enum.IsDefined(sharedConfiguration.Effect)
            ? sharedConfiguration.Effect
            : LightingEffect.Static;
        var speed = Math.Clamp(sharedConfiguration?.Speed ?? 50, 1, 100);

        return configurationList.Select(configuration =>
        {
            var copy = configuration.Copy();
            copy.Effect = effect;
            copy.Speed = speed;
            return copy;
        }).ToList();
    }

    private LightingEffect SelectedLightingEffect =>
        Enum.TryParse<LightingEffect>(SelectedEffect, out var effect) ? effect : LightingEffect.Static;

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
            speed: 50);
        yield return CreateBuiltIn(
            "Solar Zones",
            ["#FF542E", "#FFB23F", "#FF542E", "#FFD36A"],
            LightingEffect.Static,
            speed: 50);
    }

    private static LightingPreset CreateBuiltIn(
        string name,
        IReadOnlyList<string> colors,
        LightingEffect effect,
        int speed) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsBuiltIn = true,
        Zones = new List<ZoneConfiguration>
        {
            new() { ZoneName = KeyboardZones.AlphaLeft, ColorHex = colors[0], Effect = effect, Speed = speed },
            new() { ZoneName = KeyboardZones.AlphaCenter, ColorHex = colors[1], Effect = effect, Speed = speed },
            new() { ZoneName = KeyboardZones.AlphaRight, ColorHex = colors[2], Effect = effect, Speed = speed },
            new() { ZoneName = KeyboardZones.NumericKeypad, ColorHex = colors[3], Effect = effect, Speed = speed }
        }
    };
}
