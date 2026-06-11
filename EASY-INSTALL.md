# Easy Install Method

Use this when you want to give non-technical users one simple file to install MYLan.

## Windows Users

Give users this file:

```text
installer-output\windows\MYLan-Setup-2.1.0-win-x64.exe
```

They install it by double-clicking the setup file and following the wizard.

To build that file on Windows:

1. Install the .NET 8 SDK.
2. Install Inno Setup 6.
3. Double-click `CREATE-WINDOWS-INSTALLER.bat`.

The installer:

- Installs MYLan into Program Files.
- Adds a Start Menu shortcut.
- Optionally adds a desktop shortcut.
- Optionally adds a Windows Firewall rule for inbound UDP/67.
- Uninstalls cleanly through Windows Settings.

## Mac Users

You now have two Mac release options.

If building from Windows, give users this zip:

```text
installer-output/macos/MYLan-2.1.0-macos-universal-ready.zip
```

It contains both Apple Silicon and Intel builds. Users install it by:

1. Unzipping it on their Mac.
2. Opening the `macos-ready` folder.
3. Double-clicking `Install MYLan.command`.
4. Entering their Mac administrator password.

If building on a Mac, you can create DMG files:

```text
installer-output/macos/MYLan-2.1.0-osx-arm64.dmg
installer-output/macos/MYLan-2.1.0-osx-x64.dmg
```

Most newer Macs use `osx-arm64`. Older Intel Macs use `osx-x64`.

DMG users install it by opening the DMG and double-clicking `Install MYLan.command`.

After installation, users run MYLan by double-clicking:

```text
/Applications/Run MYLan.command
```

The command launcher is needed because MYLan is a DHCP server and macOS requires administrator privileges for UDP port 67 and adapter changes.

To build the universal Mac zip on Windows:

1. Install the .NET 8 SDK.
2. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-macos-zip-windows.ps1
```

To build DMGs on macOS:

1. Install the .NET 8 SDK.
2. Double-click `CREATE-MACOS-DMGS.command`.

If macOS blocks the build command, run this once in Terminal from the project folder:

```bash
chmod +x CREATE-MACOS-DMGS.command publish-macos.sh installer/macos/*.sh
```

Then double-click `CREATE-MACOS-DMGS.command` again.

## Public Release Checklist

For private/internal use, unsigned installers are usually acceptable.

For public distribution:

- Sign the Windows executable and setup installer.
- Sign the macOS app with an Apple Developer ID certificate.
- Notarise and staple the macOS DMG.
