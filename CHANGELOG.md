# Changelog

All notable changes to Dusk are listed here. Versions follow [semantic versioning](https://semver.org).

## [1.1.0] - 2026-10-04

### Added

- **Darkroom**: red only, inverted and dimmed, for reading at night. Turn it on in the tray flyout; Alt+End leaves it.

### Changed

- The warm end of the slider now reaches 600K, down from 800K. Green is gone by 700K, and below 800K the red itself dims, which is the only way left to get warmer on a screen.

## [1.0.0] - 2026-10-04

First public release.

### Added

- Screen colour temperature from 800K to 9300K, applied per monitor.
- Shortcuts: Alt+PgUp / Alt+PgDn change the current colour, Alt+End turns Dusk off for an hour, Alt+Home opens it.
- A day built from your wake time and your location's sunrise and sunset, with bedtime 9 hours before you wake.
- Three colours (Daytime, Sunset, Bedtime), six presets, and a Colors screen to set each one.
- Three views of the day: Horizon, Dial and Strata, plus Play the day to preview the whole cycle.
- Tray flyout: current colour, and turning Dusk off for an hour, until sunrise, or while a chosen app is in front.
- First-run setup that takes a city name (looked up online) or coordinates.
- Light and dark themes.
- Windows hide after 10 seconds without interaction, and stay while hovered.
- Shortcuts: Alt+PgUp / Alt+PgDn change the current colour, Alt+End turns Dusk off for an hour, Alt+Home opens it.
- Falls back to a full-screen colour filter on HDR and auto-colour-managed displays, where gamma ramps are ignored.
