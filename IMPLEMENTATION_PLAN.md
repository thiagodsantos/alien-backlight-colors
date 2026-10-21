# Alienware Command Center Desktop — Implementation Plan

## Description

Build a Windows desktop application for configuring Alienware keyboard backlighting, with the core lighting and profile-management workflow associated with Alienware Command Center. Users can choose colors and lighting effects independently for the keyboard's three alphanumeric zones and numeric keypad, preview changes, and save or apply presets.

The initial hardware target is the Dell G15 5515 keyboard using its AW-ELC USB HID controller (VID 187C, PID 0550) and AlienFX APIv4 light IDs 8–11. Keep the hardware protocol behind an adapter and report unsupported models or effects rather than sending unverified commands.

## Functionalities

### Keyboard lighting
- Detect supported Alienware keyboards and show the current device and connection status.
- Display a keyboard lighting editor with four independently configurable zones: **Left (alpha)**, **Center (alpha)**, **Right (alpha)**, and **Numeric keypad**.
- Set a color for each zone using a color picker and a hex/RGB value input.
- Select a lighting style per zone, starting with **Static** and adding supported effects such as **Breathing**, **Color Cycle**, and **Wave** when the device API exposes them.
- Configure effect options where supported, such as speed and direction.
- Preview the selected colors and styles in the app before applying them.
- Apply changes to the connected keyboard and provide clear success or actionable error feedback.
- Provide a 0–100% global brightness control and an immediate backlight on/off toggle.
- Provide an option to restore the device's default lighting where supported.

### Presets
- Save the current four-zone colors, global brightness, and backlight on/off state as a named preset.
- List, select, apply, rename, and delete saved presets.
- Include a small set of optional built-in presets; keep user presets separate from built-ins.
- Persist presets locally so they remain available after the app closes or restarts.
- Validate preset data and gracefully handle missing, unsupported, or outdated settings.

### Desktop application experience
- Provide a native Windows desktop window with a clear lighting-editor layout.
- Show device availability and distinguish previewed settings from settings applied to hardware.
- Keep the editor and preset management usable when no supported keyboard is connected; hardware-only actions should explain why they are unavailable.
- Support keyboard navigation, readable contrast, and sensible window resizing.

## Tech stack

- **Language/runtime:** C# on modern .NET (target a supported LTS release).
- **Desktop UI:** WPF for a native Windows application and mature desktop controls.
- **Application structure:** MVVM, using `CommunityToolkit.Mvvm` for observable view models and commands.
- **Hardware integration:** A dedicated `IKeyboardLightingService` abstraction with a Windows HID adapter for the verified G15 5515 APIv4 controller. Independent static RGB uses light IDs 8–11 and global brightness uses APIv4 dimming reports; dynamic hardware effects and other layouts remain unsupported until validated.
- **Preset storage:** Versioned JSON files in the user's local application-data directory, with a repository/service layer for validation and persistence.
- **Testing:** xUnit for domain and service tests; mock the hardware service for UI-facing and application-logic tests.
- **Packaging:** MSIX or a signed Windows installer after validating the deployment and driver/API requirements.

## Suggested application design

- **Presentation:** WPF views for the main lighting editor, zone controls, preview, and preset management.
- **Application/domain:** Models for `LightingPreset`, `ZoneConfiguration`, `LightingEffect`, and supported device capabilities; commands for preview, apply, reset, and preset operations.
- **Infrastructure:** Hardware-service adapter and preset-storage implementation. Keep API-specific types out of the view models and domain models.
- **Capability handling:** Query supported zones and effects where possible; disable or explain unavailable controls instead of silently applying unsupported settings.

## Implementation phases

1. **Validate hardware access:** Verified the local Dell G15 5515 AW-ELC controller (USB VID 187C, PID 0550), platform query, and AlienFX APIv4 light IDs 8–11. Use the tested APIv4 static-color command for independent zones; keep unverified animation protocols disabled on this hardware.
2. **Build the application shell:** Create the WPF app, MVVM structure, device-status view, and a mock lighting service for development.
3. **Implement the editor and preview:** Add four-zone color/style editing, validation, and a visual preview that works without connected hardware.
4. **Integrate device control:** Implemented the Windows HID adapter, AW-ELC capability detection, APIv4 per-zone color apply/reset, and user-facing errors. Validate writes on the target device and separately verify an animation protocol before enabling dynamic hardware effects.
5. **Add preset management:** Implement local versioned storage and save/list/apply/rename/delete workflows; add migration or fallback behavior for invalid preset files.
6. **Harden and package:** Add automated tests, accessibility and resize checks, installer/package configuration, and setup documentation including supported-device limitations.

## Initial acceptance criteria

- Users can edit the left, center, and right alphanumeric zones and the numeric keypad independently and preview the result.
- Users can choose and apply an independent static RGB color for all four zones on a detected G15 5515 AW-ELC device; unsupported hardware effects are reported clearly.
- Users can adjust brightness from 0–100%, toggle the backlight, and save/reopen presets that include all four zones, brightness, and power state.
- The app reports unsupported devices, unavailable effects, and apply failures clearly without losing the user's configuration.
- Core editing and preset workflows can be tested without physical hardware through the mock lighting service.
