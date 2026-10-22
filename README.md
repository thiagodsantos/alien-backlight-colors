# Alien Backlight Colors

A Windows desktop editor for the four lighting zones on Dell G15 laptops using the Alien AW-ELC controller.

## Supported hardware

- Verified target: **Dell G15 5515**, AW-ELC controller `USB VID 187C / PID 0550`, AlienFX APIv4.
- On startup, the app reads the connected Dell HID controller's VID/PID automatically and verifies its platform report. It supports the verified 16-LED layout; other controller layouts remain unsupported.
- The four UI zones are **Left (alpha)**, **Center (alpha)**, **Right (alpha)**, and **Numeric keypad**.

## Build and run

Requires the .NET 8 SDK on Windows:

```powershell
dotnet build AlienCommandCenter.csproj
dotnet run --project AlienCommandCenter.csproj
```

## GitHub Actions releases

- `.github/workflows/build-release.yml` builds the app on pushes, pull requests, and manual runs.
- To publish automatically, bump `VERSION` and add a matching `## [vX.Y.Z]` section to `CHANGELOG.md`.
- Push the change to `main`, or run **Actions → Build and Release → Run workflow** from `main`. If the version tag does not exist, the workflow creates `v<VERSION>` and publishes a GitHub Release with the matching changelog section and a self-contained Windows x64 ZIP.
- If `v<VERSION>` already exists, the workflow performs a build only; bump `VERSION` for the next release.

If the controller is available to Windows HID, the device badge shows **DEVICE CONNECTED**. Otherwise, the app remains usable for editing and presets in preview mode and displays the controller error.

## Hardware behavior

- **Apply to keyboard** and **Apply preset** write each zone independently through AlienFX APIv4 light IDs `8`, `9`, `10`, and `11`, then send the APIv4 dimming report.
- On the verified G15 5515 APIv4 controller, hardware writes currently support **Static** RGB per zone. Breathing, Color Cycle, and Wave remain preview-only; attempting to apply them reports that limitation because their hardware protocol is not yet verified.
- Presets are stored locally on the PC. The app does not overwrite AWCC/NVRAM lighting profiles.

## Brightness and backlight toggle

- Set brightness from **0% to 100%** with the global slider. Slider changes are pending until **Apply to keyboard** or **Apply preset** is used; both actions turn on the backlight (using 100% if brightness is 0) and apply the colors.
- **Turn backlight off/on** applies immediately. Turning the light back on restores the selected slider brightness; `0%` sends the light-off state. Use this toggle to turn the backlight off explicitly.
- When a supported keyboard is detected, the app restores the last successfully applied lighting on startup. Set **Backlight active for (seconds)** to turn it off after keyboard inactivity; `0` keeps it active while the app is open or running in the notification area; in this mode, the app reapplies the saved lighting and reopens the controller every 30 seconds as a keep-alive. Pressing a key restores the last applied colors and restarts the timeout. Closing the window hides the app in the notification area, where it must remain running to handle keyboard activity. Use the tray menu's **Exit** command to stop it; after exiting, the app cannot respond to keyboard input until launched again. The last applied lighting and timeout setting are saved in the current user's registry.
- Saved presets retain brightness and on/off state when loaded into the editor; applying a preset always turns the backlight on.
- Direct HID access is blocked while Alien Command Center (AWCC) components are registered because they can own the same controller. Use **Remove AWCC** to launch Dell's registered uninstaller; the app requests confirmation and UAC.
- **Restore default** applies static red at 100% brightness to all four zones; it does not perform a firmware factory reset.
- **Remove AWCC** asks for confirmation and launches the registered Dell AWCC Package Manager uninstaller with UAC. It does not force-delete drivers, services, registry keys, or residual files; removing AWCC can also remove Dell lighting/thermal features.

## Troubleshooting

If Windows shows the AW-ELC as **Unknown USB Device (Code 43)** after AWCC removal, fully shut down the laptop, unplug AC power, hold the power button for 30 seconds, then reconnect power and boot. This resets the embedded controller; do not force-delete its USB driver.

The AW-ELC protocol is model-specific, reverse-engineered, and not a Dell-supported SDK. Protocol reference and license attribution are in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
