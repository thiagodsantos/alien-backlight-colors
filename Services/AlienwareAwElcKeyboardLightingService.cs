using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Win32;
using AlienCommandCenter.Models;

namespace AlienCommandCenter.Services;

public sealed class AlienwareAwElcKeyboardLightingService : IKeyboardLightingService, IDisposable
{
    private static readonly byte[] ApiV4LightIds = [8, 9, 10, 11];
    private static readonly SemaphoreSlim ControllerLock = new(1, 1);
    private ControllerConnection? _controller;
    private bool _disposed;

    public KeyboardDeviceStatus GetStatus()
    {
        var awccRegistration = FindAwccRegistration();
        if (awccRegistration is not null)
        {
            return new KeyboardDeviceStatus(
                IsConnected: false,
                DeviceName: "AWCC installation may conflict",
                Message: BuildAwccConflictMessage(awccRegistration));
        }

        ControllerLock.Wait();
        try
        {
            var controller = GetOrOpenController();
            return new KeyboardDeviceStatus(
                IsConnected: true,
                DeviceName: "AW-ELC AlienFX APIv4 keyboard",
                Message: "HID controller opened in AlienFX APIv4 mode. Independent static colors use light IDs 8–11; animated effects are preview-only on this controller.");
        }
        catch (FileNotFoundException)
        {
            return new KeyboardDeviceStatus(
                IsConnected: false,
                DeviceName: "AW-ELC keyboard not enumerated",
                Message: "Windows cannot currently see the AW-ELC HID controller (VID 187C, PID 0550). If Device Manager shows USB Code 43 after AWCC removal, perform a full EC power reset: shut down, unplug AC, hold the power button for 30 seconds, then reconnect and boot.");
        }
        catch (NotSupportedException exception)
        {
            return new KeyboardDeviceStatus(
                IsConnected: false,
                DeviceName: "Unsupported AW-ELC platform",
                Message: exception.Message);
        }
        catch (Exception exception)
        {
            return new KeyboardDeviceStatus(
                IsConnected: false,
                DeviceName: "AW-ELC controller unavailable",
                Message: $"The controller was found but could not be opened: {exception.GetBaseException().Message}");
        }
        finally
        {
            ControllerLock.Release();
        }
    }

    public Task<HardwareResult> ApplyAsync(IReadOnlyList<ZoneConfiguration> zones, int brightnessPercent)
    {
        var snapshot = zones.Select(zone => zone.Copy()).ToArray();
        return Task.Run(() => ApplyNow(snapshot, brightnessPercent));
    }

    public Task<HardwareResult> ResetAsync()
    {
        var defaults = KeyboardZones.Ordered
            .Select(zoneName => new ZoneConfiguration
            {
                ZoneName = zoneName,
                ColorHex = "#FF0000",
                SecondaryColorHex = "#000000",
                Effect = LightingEffect.Static,
                Speed = 50
            })
            .ToArray();
        return ApplyAsync(defaults, brightnessPercent: 100);
    }

    private HardwareResult ApplyNow(IReadOnlyList<ZoneConfiguration> zones, int brightnessPercent)
    {
        if (brightnessPercent is < 0 or > 100)
        {
            return new HardwareResult(false, "Brightness must be between 0 and 100 percent.");
        }

        var awccRegistration = FindAwccRegistration();
        if (awccRegistration is not null)
        {
            return new HardwareResult(false, BuildAwccConflictMessage(awccRegistration));
        }

        if (!TryOrderZones(zones, out var orderedZones, out var validationError))
        {
            return new HardwareResult(false, validationError);
        }

        if (brightnessPercent > 0 && orderedZones.Any(zone => zone.Effect != LightingEffect.Static))
        {
            return new HardwareResult(
                false,
                "The verified G15 5515 APIv4 interface supports independent static zone colors. Select Static on all zones; animated effects are preview-only for this device.");
        }

        ControllerLock.Wait();
        try
        {
            var controller = GetOrOpenController();
            ApplyApiV4ZoneColors(controller, orderedZones, brightnessPercent);
            return new HardwareResult(true, $"Applied the four zone colors independently at {brightnessPercent}% brightness.");
        }
        catch (Exception exception)
        {
            DisposeControllerHandle();
            return new HardwareResult(false, $"Could not apply keyboard lighting: {exception.GetBaseException().Message}");
        }
        finally
        {
            ControllerLock.Release();
        }
    }

    private ControllerConnection GetOrOpenController()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_controller is not null)
        {
            return _controller;
        }

        _controller = OpenController();
        return _controller;
    }

    private static ControllerConnection OpenController()
    {
        var paths = WindowsHidTransport.FindAwElcDevicePaths();
        if (paths.Count == 0)
        {
            throw new FileNotFoundException("No HID device with USB VID 187C and PID 0550 was found.");
        }

        Exception? lastError = null;
        foreach (var path in paths)
        {
            WindowsHidTransport? transport = null;
            try
            {
                using (var probe = WindowsHidTransport.Open(path))
                {
                    var response = ExtractPlatformResponse(probe.SendAndRead(0x20, 0x02));
                    if (response.Length < 5 || response[0] != 0x20 || response[1] != 0x02 || response[4] != 16)
                    {
                        throw new InvalidDataException("This HID interface did not identify the verified 16-LED G15 5515 AW-ELC platform.");
                    }
                }

                transport = WindowsHidTransport.Open(path);
                transport.SendOutputOnly(0x21, 0x00, 0x04, 0xFF, 0xFF);
                transport.SendOutputOnly(0x21, 0x00, 0x01, 0xFF, 0xFF);
                return new ControllerConnection(transport);
            }
            catch (Exception exception)
            {
                transport?.Dispose();
                lastError = exception;
            }
        }

        throw new InvalidOperationException(
            "AW-ELC HID interfaces were found, but none passed the G15 5515 check and APIv4 edit-mode commands. Close other keyboard-lighting software and check device permissions.",
            lastError);
    }

    private static byte[] ExtractPlatformResponse(byte[] report)
    {
        var offset = report.Length >= 2 && report[0] == 0x00 && report[1] == 0x03
            ? 2
            : report.Length > 0 && report[0] == 0x00
                ? 1
                : report.Length > 0 && report[0] == 0x03
                    ? 1
                    : 0;
        return report.AsSpan(offset).ToArray();
    }

    private static void ApplyApiV4ZoneColors(
        ControllerConnection controller,
        IReadOnlyList<ZoneConfiguration> zones,
        int brightnessPercent)
    {
        for (var index = 0; index < zones.Count; index++)
        {
            var color = brightnessPercent == 0 ? new RgbColor(0, 0, 0) : ParseColor(zones[index].ColorHex);
            controller.Transport.SendOutputOnly(
                0x27,
                color.Red,
                color.Green,
                color.Blue,
                0x00,
                0x01,
                ApiV4LightIds[index]);
        }

        var applyBrightness = new byte[4 + ApiV4LightIds.Length];
        applyBrightness[0] = 0x26;
        applyBrightness[1] = (byte)(100 - brightnessPercent);
        applyBrightness[2] = 0x00;
        applyBrightness[3] = (byte)ApiV4LightIds.Length;
        ApiV4LightIds.CopyTo(applyBrightness, 4);
        controller.Transport.SendOutputOnly(applyBrightness);
    }

    private void DisposeControllerHandle()
    {
        _controller?.Dispose();
        _controller = null;
    }

    public void Dispose()
    {
        ControllerLock.Wait();
        try
        {
            DisposeControllerHandle();
            _disposed = true;
        }
        finally
        {
            ControllerLock.Release();
        }
    }

    private static string BuildAwccConflictMessage(string registration) =>
        $"AWCC components are registered ({registration}) and may conflict with direct APIv4 HID control. Close or uninstall/disable AWCC, then restart this app. No keyboard color command was sent.";

    private static string? FindAwccRegistration()
    {
        string[] uninstallPaths =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        ];
        RegistryKey[] roots = [Registry.LocalMachine, Registry.CurrentUser];

        foreach (var root in roots)
        {
            foreach (var uninstallPath in uninstallPaths)
            {
                using var uninstallKey = root.OpenSubKey(uninstallPath);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var keyName in uninstallKey.GetSubKeyNames())
                {
                    using var productKey = uninstallKey.OpenSubKey(keyName);
                    var displayName = productKey?.GetValue("DisplayName") as string;
                    var installLocation = productKey?.GetValue("InstallLocation") as string;
                    var isAwcc = displayName?.Contains("Alienware Command Center", StringComparison.OrdinalIgnoreCase) == true ||
                        displayName?.Contains("AWCC", StringComparison.OrdinalIgnoreCase) == true ||
                        installLocation?.Contains(@"Alienware\Alienware Command Center", StringComparison.OrdinalIgnoreCase) == true;

                    if (isAwcc)
                    {
                        return string.IsNullOrWhiteSpace(displayName) ? installLocation ?? keyName : displayName;
                    }
                }
            }
        }

        return null;
    }

    private static RgbColor ParseColor(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        if (value.Length != 6 || !value.All(Uri.IsHexDigit))
        {
            throw new FormatException($"'{hex}' is not a valid six-digit RGB color.");
        }

        return new RgbColor(
            byte.Parse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static bool TryOrderZones(
        IReadOnlyList<ZoneConfiguration> zones,
        out ZoneConfiguration[] orderedZones,
        out string error)
    {
        orderedZones = [];
        if (zones.Count != KeyboardZones.Ordered.Count)
        {
            error = "The keyboard controller requires exactly four zone configurations.";
            return false;
        }

        var byName = new Dictionary<string, ZoneConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in zones)
        {
            if (!byName.TryAdd(zone.ZoneName, zone))
            {
                error = $"Duplicate keyboard zone '{zone.ZoneName}'.";
                return false;
            }
        }

        foreach (var name in KeyboardZones.Ordered)
        {
            if (!byName.ContainsKey(name))
            {
                error = $"The configuration is missing the '{name}' zone.";
                return false;
            }
        }

        orderedZones = KeyboardZones.Ordered.Select(name => byName[name]).ToArray();
        error = string.Empty;
        return true;
    }

    private sealed record ControllerConnection(WindowsHidTransport Transport) : IDisposable
    {
        public void Dispose() => Transport.Dispose();
    }

    private readonly record struct RgbColor(byte Red, byte Green, byte Blue);
}
