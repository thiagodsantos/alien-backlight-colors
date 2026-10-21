using AlienCommandCenter.Models;

namespace AlienCommandCenter.Services;

public sealed record KeyboardDeviceStatus(bool IsConnected, string DeviceName, string Message);

public sealed record HardwareResult(bool Succeeded, string Message);

public interface IKeyboardLightingService
{
    KeyboardDeviceStatus GetStatus();
    Task<HardwareResult> ApplyAsync(IReadOnlyList<ZoneConfiguration> zones, int brightnessPercent);
    Task<HardwareResult> ResetAsync();
}
