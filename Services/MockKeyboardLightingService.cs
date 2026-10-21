using AlienCommandCenter.Models;

namespace AlienCommandCenter.Services;

/// <summary>
/// Development fallback used until a supported Alienware device API is selected.
/// </summary>
public sealed class MockKeyboardLightingService : IKeyboardLightingService
{
    public KeyboardDeviceStatus GetStatus() => new(
        IsConnected: false,
        DeviceName: "Hardware integration not configured",
        Message: "Editor and presets run in preview mode. Physical-device detection and lighting apply are disabled until a supported Alienware API is integrated.");

    public Task<HardwareResult> ApplyAsync(IReadOnlyList<ZoneConfiguration> zones, int brightnessPercent) => Task.FromResult(
        new HardwareResult(false, "Hardware control is not configured. Your lighting remains available in the editor and presets."));

    public Task<HardwareResult> ResetAsync() => Task.FromResult(
        new HardwareResult(false, "Hardware control is not configured, so device lighting cannot be reset."));

    public Task ConfigureBacklightActiveTimeoutAsync(int seconds) => Task.CompletedTask;

    public Task RefreshBacklightAsync() => Task.CompletedTask;
}
