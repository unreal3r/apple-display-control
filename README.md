# Apple Display Control

A small, portable Windows app for **Apple Studio Display** and **Studio Display XDR**, with an Apple keyboard function-key preference. It talks directly to the display and keyboard drivers; it does not launch the Boot Camp control panel.

The app, tray popup, menus and dialogs use dark mode by default. No theme toggle or extra UI packages are required.

Version 0.2.2 enables native per-monitor high-DPI rendering, including vector sliders and icons, and avoids rebuilding or greying out the interface during background display polling. The executable creates its small `.exe.config` file beside itself on first run so DPI support also works after an in-app upgrade or filename change. Keep that file beside the executable; no separate setup is needed.

## Use

Open **Apple Display Control.exe**. Choose a display, then adjust brightness or choose a reference preset. Brightness changes are silent. Closing the window leaves the app in the notification area; choose **Quit** to stop it. Double-click the notification icon or run the app again to reopen it.

**Single-click the notification icon** for a compact brightness popup with one slider per connected display. Click outside it or press Escape to dismiss it. **Double-click** opens the full app. Right-click keeps the usual menu.

**App updates…** in the full window, or **Check for updates…** in the right-click menu, checks this project's stable GitHub releases. Choose **Download and restart** to install a newer version. Updates preserve your settings and startup shortcut, run without administrator access, and keep the previous executable as a backup. Version 0.1.0 needs one manual upgrade to get this feature.

- Separate brightness and reference presets for each connected display.
- A **100%** button and a notification-area command to set all displays to maximum.
- Detects displays connected while the app is running, including long after login.
- Optional maximum brightness on connection. Applies once when each display is first seen, rather than overriding subsequent manual changes.
- Optional launch at login, minimized to the notification area.
- Reads preset names and descriptions from the display, rather than hardcoding a list.
- Preset changes automatically revert after 15 seconds unless confirmed. Switching is paused while any Windows output has HDR enabled, or if HDR/current-preset information cannot be read.
- Apple keyboard **Use F1, F2, etc. as standard function keys** preference. Hold Fn for the other action. Saving asks Windows for administrator approval, updates the Apple keyboard driver, and stores the preference for reconnection/restart. Connected supported keyboards receive the change immediately.

The keyboard checkbox matches Boot Camp: **checked → F1 acts as F1; unchecked → F1 acts as brightness down**. Version 0.2.1 corrects the reversed live-driver mapping in earlier versions. Both the live driver and saved `OSXFnBehavior` flag use 0 for standard keys and 1 for media keys.

Keep the executable in a permanent folder before enabling launch at login. Settings and bounded diagnostic logs are stored beside it, so that folder must be writable. You can disable launch at login using the same checkbox.

## Supported hardware and limits

| Hardware | Status |
| --- | --- |
| Studio Display XDR (2026), USB `05AC:1116` | Brightness, all 16 presets discovered, and same-value write/readback tested on hardware |
| Studio Display (2022), USB `05AC:1114` | Implemented using its native HID brightness/preset interfaces; physical verification pending |
| Studio Display (Gen 2), USB `05AC:1118` | Recognized; physical verification pending |
| Apple Magic Keyboard with KeyMagic2 | Live mode query and same-value write/readback tested; physical key behavior after a mode change still needs user verification |
| Other Apple keyboard drivers | Recognized control-device names; not physically verified |

Windows 10/11 x64 and .NET Framework 4.8 are required. The app uses native Windows APIs with no downloaded runtime dependencies. A USB data connection to the display is required, normally through Thunderbolt/USB-C.

This replaces the controls above, **not Apple's drivers**. Keep Apple's installed keyboard driver for the Fn preference and hardware/media-key handling. The app does not install, patch, or redistribute Apple drivers. It does not depend on the Boot Camp control-panel executable.

Automatic adjustment using the ambient light sensor, True Tone, screen rotation, and arbitrary white-point/calibration editing are not included. In particular, the Studio Display XDR's light-sensor interface is not accessible through the default Windows HID driver. The app leaves Windows HDR, resolution, refresh rate, and color-profile settings alone. Reference presets may fix brightness by design; the slider is disabled for those presets. “100%” means the display's reported brightness-control maximum, not a promise of a particular HDR peak luminance.

Apple driver behavior can vary by model/version. This utility cannot guarantee compatibility with future Windows releases or replace drivers that Windows stops loading. No claim is made here that Microsoft has announced a Boot Camp control-panel deprecation.

## Build

Run `build.cmd` on Windows. It uses the .NET Framework compiler included with Windows and produces `Apple Display Control.exe`. No NuGet packages are required.

```powershell
.\build.cmd
```

Source files:

- `Hid.cs`: HID discovery, per-device Container ID pairing, brightness and reference-preset controls.
- `SystemControls.cs`: Windows HDR query and Apple keyboard driver access.
- `App.cs`: window, notification area, connection detection, startup preference, and diagnostics.
- `QuickBrightness.cs`: compact sliders and single/double-click handling.
- `Updates.cs`: fixed-repository update checks, verified downloads, and atomic app replacement.
- `Theme.cs`: shared dark colors and compact, keyboard-accessible sliders.
- `Dpi.cs`, `app.config`, `app.manifest`: Windows high-DPI configuration and startup handling.

Run `test.cmd` for the tray, multi-display, updater and security checks. `test.cmd --online` additionally checks the live GitHub release API. The installation test uses isolated fixture executables and verifies replacement, restart, backup, and preservation of settings; it does not replace your installed app.

## Security boundaries

The app opens no listening port and accepts no remote commands. The updater contacts only GitHub for this repository's release metadata and assets. It checks HTTPS, the exact asset address, file size and GitHub's SHA-256 digest before running a download. The replacement helper accepts only this app's filenames in a dedicated staging folder, rejects symbolic links and redirected folders, and uses an atomic replacement with recovery. It never invokes a command shell or requests elevation. Administrator approval is reserved for the existing Apple keyboard-driver preference.

Updates trust this repository and GitHub's HTTPS service. They are not independently code-signed. This design and its tests reduce risk; they do not establish that the software has no vulnerabilities.

## Diagnostics and verification

```powershell
# Read display/keyboard information and enumerate preset metadata.
& '.\Apple Display Control.exe' --diagnose

# Write each currently selected value back unchanged and verify its readback.
# Does not test a different brightness, reference preset, or physical key mode.
& '.\Apple Display Control.exe' --verify
```

These commands create `diagnostics.txt` and `verification.txt`. Normal errors are recorded in `display-control.log`. Logs and local settings are excluded from Git.

`--tray` opens without a main window. `--render-review` generates an off-screen layout preview for development. `--keyboard-mode standard|media` is the app's elevated keyboard-settings helper.

## Protocol references

The implementation is original C# using the public Windows HID API. The following primary references describe the device protocol and Windows structures:

- [StudioBrightness++ HID interface map](https://github.com/LitteRabbit-37/Studio-Brightness-PlusPlus/blob/main/docs/hid-map.md)
- [KeyMagic driver research](https://github.com/VladislavEkimtcov/macbook-windows-swap-fn-ctrl/blob/main/docs/KeyMagic-internals.md)
- [Microsoft: HIDP_VALUE_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_value_caps)
- [Microsoft: HidP_SetUsageValue](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_setusagevalue)
- [Apple: function-key behavior in Boot Camp](https://support.apple.com/guide/bootcamp-control-panel/bcmp423a8bcd/mac)

Unofficial project; not affiliated with Apple or Microsoft.

## License

MIT. See [LICENSE](LICENSE).
