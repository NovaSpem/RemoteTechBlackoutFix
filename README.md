#RemoteTech BlackoutFix

A fix-mod for Kerbal Space Program that adds realistic attenuation to RemoteTech's radio signal upon re-entry into the atmosphere.

## Features
- Firefly integration for plasma intensity detection
- Smooth fade-out of RemoteTech antennas upon atmospheric entry
- Configurable thresholds via config
- Connection loss and re-establishment messages

## Dependencies
- **RemoteTech** (required)
- **Firefly** (required for plasma effects)

## Settings
The config is located in `GameData/RemoteTechBlackoutFix/RemoteTechBlackoutFix.cfg`

Parameters:
- `forceEnable` - Forces mod enablement
- `plasmaMinValue` - Fade-out threshold (default: 350)
- `plasmaMaxValue` - Complete connection loss threshold (default: 2000)
- `showMessages` - Show on-screen messages
- `debugLog` - Debug logging

## License
MIT
