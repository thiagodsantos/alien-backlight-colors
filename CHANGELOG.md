# Changelog

Release notes are written under the exact version tag heading. The release workflow publishes the matching section only.

## [v1.3.0]

### Added
- Added a neon alien logo to the application header and window icons.
- Bundled Goldman and Quantico fonts for offline use.

### Changed
- Updated the interface palette and color picker swatches to match the logo's neon colors.
- Applied Goldman to the application name and Quantico to the rest of the interface; changed the default zone color to cyan.

## [v1.2.0]

### Changed
- Renamed the application to Alien Backlight Colors.

## [v1.1.0]

### Added
- Notification-area tray support keeps the app running when its window is closed and provides an explicit Exit command.
- Persistent lighting restores the last successfully applied colors and backlight timeout from the current user's registry when the app starts.
- Configurable keyboard-inactivity timeout and controller keep-alive behavior for supported keyboards.

### Changed
- Applying lighting or a preset now turns the backlight on; presets retain their brightness and on/off state when loaded into the editor.

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
