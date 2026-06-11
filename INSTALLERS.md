# MYLan Installers and Uninstallers

This project includes installer/uninstaller tooling for Windows and macOS.

## Windows Installer

The Windows route uses Inno Setup 6.

Build from the repository root:

```powershell
.\build-installer-windows.ps1
```

This will:

1. Publish the standalone Windows x64 app.
2. Build an installer using `installer/windows/MYLan.iss`.
3. Place the installer at `installer-output\windows\MYLan-Setup-2.1.0-win-x64.exe`.

The installer:

- Installs MYLan to `Program Files\Event Tech Research\MYLan`.
- Adds a Start Menu shortcut.
- Optionally adds a desktop shortcut.
- Optionally adds a Windows Firewall rule limited to inbound UDP/67 for `MYLan.exe`.
- Registers a normal Windows uninstaller under Apps & Features.

Uninstall through:

```text
Windows Settings > Apps > Installed apps > MYLan > Uninstall
```

## macOS App Bundle and DMG

Build app executables on macOS:

```bash
./publish-macos.sh
```

Create `.app` bundles:

```bash
./installer/macos/create-macos-app.sh
```

Outputs:

```text
installer-output/macos/osx-x64/MYLan.app
installer-output/macos/osx-arm64/MYLan.app
```

Install manually:

```bash
sudo ./installer/macos/install-macos.sh ./installer-output/macos/osx-arm64/MYLan.app
```

For Intel Macs, use:

```bash
sudo ./installer/macos/install-macos.sh ./installer-output/macos/osx-x64/MYLan.app
```

Run:

```bash
mylan
```

or:

```bash
sudo /Applications/MYLan.app/Contents/MacOS/MYLan
```

Uninstall:

```bash
sudo ./installer/macos/uninstall-macos.sh
```

Create DMG files:

```bash
./installer/macos/create-dmg-macos.sh
```

Outputs:

```text
installer-output/macos/MYLan-2.1.0-osx-x64.dmg
installer-output/macos/MYLan-2.1.0-osx-arm64.dmg
```

## Public Release

For private/internal testing, unsigned installers are acceptable.

For public release:

1. Sign the Windows executable and installer.
2. Code-sign the macOS `.app` with an Apple Developer ID certificate.
3. Notarise the macOS artifact with Apple.
4. Staple the notarisation ticket.
