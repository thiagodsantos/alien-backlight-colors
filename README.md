# Alienware Keyboard Studio

A Windows desktop editor for the four lighting zones on Dell G15 laptops using the Alienware AW-ELC controller.

## Supported hardware

- Verified target: **Dell G15 5515**, AW-ELC controller `USB VID 187C / PID 0550`, AlienFX APIv4.
- The app queries the controller's platform report on startup and supports the verified 16-LED layout. Other controller layouts remain unsupported.
- The four UI zones are **Left (alpha)**, **Center (alpha)**, **Right (alpha)**, and **Numeric keypad**.

## Build and run

Requires the .NET 8 SDK on Windows:

```powershell
dotnet build AlienCommandCenter.csproj
dotnet run --project AlienCommandCenter.csproj
```

## GitHub Actions releases

- `.github/workflows/build-release.yml` builds the app on pushes, pull requests, and manual runs.
- To publish automatically, add a `## [vX.Y.Z]` section to `CHANGELOG.md`, then push a matching `v*` tag such as `v1.0.0`.
- To publish manually, open **Actions → Build and Release → Run workflow** and enter an existing tag in `release_tag`. Leaving it blank runs a build only, so the release job is skipped.
- The workflow checks out the selected tag and publishes its matching changelog section plus a self-contained Windows x64 ZIP as a GitHub Release asset.

If the controller is available to Windows HID, the device badge shows **DEVICE CONNECTED**. Otherwise, the app remains usable for editing and presets in preview mode and displays the controller error.

## Hardware behavior

- **Apply to keyboard** and **Apply preset** write each zone independently through AlienFX APIv4 light IDs `8`, `9`, `10`, and `11`, then send the APIv4 dimming report.
- On the verified G15 5515 APIv4 controller, hardware writes currently support **Static** RGB per zone. Breathing, Color Cycle, and Wave remain preview-only; attempting to apply them reports that limitation because their hardware protocol is not yet verified.
- Presets are stored locally on the PC. The app does not overwrite AWCC/NVRAM lighting profiles.

## Brightness and backlight toggle

- Set brightness from **0% to 100%** with the global slider. Slider changes are pending until **Apply to keyboard** or **Apply preset** is used.
- **Turn backlight off/on** applies immediately. Turning the light back on restores the selected slider brightness; `0%` sends the light-off state.
- Saved presets include both brightness and the on/off state.
- Direct HID access is blocked while Alienware Command Center components are registered, because AWCC can own the same controller. Use **Remove AWCC** to launch Dell's registered uninstaller; the app requests confirmation and UAC.
- **Restore default** applies static red at 100% brightness to all four zones; it does not perform a firmware factory reset.
- **Remove AWCC** asks for confirmation and launches the registered Dell AWCC Package Manager uninstaller with UAC. It does not force-delete drivers, services, registry keys, or residual files; removing AWCC can also remove Dell lighting/thermal features.

## Troubleshooting

If Windows shows the AW-ELC as **Unknown USB Device (Code 43)** after AWCC removal, fully shut down the laptop, unplug AC power, hold the power button for 30 seconds, then reconnect power and boot. This resets the embedded controller; do not force-delete its USB driver.

The AW-ELC protocol is model-specific, reverse-engineered, and not a Dell-supported SDK. Protocol reference and license attribution are in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
