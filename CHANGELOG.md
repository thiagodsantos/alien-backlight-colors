# Changelog

Release notes are written under the exact version tag heading. The release workflow publishes the matching section only.

## [v1.1.0]

## [v1.0.0]

### Added
- WPF desktop lighting editor for four independently configurable keyboard zones: left alpha, center alpha, right alpha, and numeric keypad.
- Windows HID detection and AlienFX APIv4 static RGB output for the verified Dell G15 5515 AW-ELC controller (light IDs 8–11).
- Global 0–100% brightness control and immediate backlight on/off toggle.
- Local presets that store zone colors, brightness, and backlight state.
- Confirmed AWCC Package Manager removal button with UAC elevation and scoped uninstallation.

### Limitations
- Breathing, Color Cycle, and Wave are preview-only on the verified APIv4 controller.
- Other keyboard/controller layouts remain unsupported until their protocol is verified.
