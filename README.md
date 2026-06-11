# MYLan DHCP Field Server

MYLan is a lightweight .NET 8 Avalonia desktop app for creating a temporary DHCP server on an isolated field network. It is intended for production, AV, lighting, Dante, embedded-control, and lab networks where a laptop needs to provide quick IP assignment without enterprise infrastructure.

## What It Does

- Lists active non-loopback network adapters.
- Applies a temporary static IPv4 address to the selected adapter.
- Runs a DHCP server on UDP port 67.
- Serves IPv4 leases from a configurable pool.
- Sends DHCP replies to the configured subnet broadcast address.
- Persists active leases between sessions.
- Attempts to restore the previous adapter IP configuration when stopped or when startup fails.

## Safety Notes

MYLan is a DHCP server. Run it only on an isolated or intentionally managed network.

- Administrator/root privileges are required.
- The app changes the selected adapter's IPv4 configuration.
- Automatic adapter restore is best-effort because Windows, macOS, and Linux expose network settings differently.
- MYLan ignores DHCPREQUEST packets explicitly addressed to another DHCP server.
- MYLan does not NAK outside-pool requests unless the request explicitly names MYLan as the target DHCP server.
- Public distribution should use signed Windows installers and Apple Developer ID signing/notarisation for macOS.

## Requirements

Build requirements:

- .NET 8 SDK
- Windows, macOS, or Linux for normal development builds
- Inno Setup 6 for the Windows installer
- macOS with `hdiutil` for DMG creation

Runtime requirements:

- Windows: run as Administrator
- macOS/Linux: run as root, usually through `sudo`
- UDP ports 67 and 68 must not be blocked by local firewall policy

## Build

Restore and build:

```powershell
dotnet restore .\MYLan\MYLan.csproj
dotnet build .\MYLan\MYLan.csproj -c Release
```

For a simple end-user install workflow, see `EASY-INSTALL.md`.

To create a Mac-ready zip from Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-macos-zip-windows.ps1
```

Output:

```text
installer-output\macos\MYLan-2.1.0-macos-universal-ready.zip
```

Publish Windows x64:

```powershell
dotnet publish .\MYLan\MYLan.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\release\win-x64
```

Publish macOS Intel and Apple Silicon:

```bash
./publish-macos.sh
```

## Windows Installer

Build the Windows installer from the repository root:

```powershell
.\build-installer-windows.ps1
```

Output:

```text
installer-output\windows\MYLan-Setup-2.1.0-win-x64.exe
```

The installer:

- Installs MYLan under `Program Files\Event Tech Research\MYLan`.
- Creates Start Menu shortcuts.
- Optionally creates a desktop shortcut.
- Optionally adds a Windows Firewall rule limited to inbound UDP/67 for `MYLan.exe`.
- Registers a normal Windows uninstaller.

## macOS App and DMG

Build app binaries:

```bash
./publish-macos.sh
```

Create `.app` bundles:

```bash
./installer/macos/create-macos-app.sh
```

Install one bundle:

```bash
sudo ./installer/macos/install-macos.sh ./installer-output/macos/osx-arm64/MYLan.app
```

Create DMG files:

```bash
./installer/macos/create-dmg-macos.sh
```

The provided macOS tooling is suitable for internal testing. Public release should add Developer ID signing, notarisation, and stapling.

## Lease Storage

MYLan stores leases in per-user application data:

```text
MYLan/mylan-leases.json
```

If the platform app-data folder cannot be created, it falls back to:

```text
~/.mylan/mylan-leases.json
```

Only active, unexpired leases and currently quarantined declined IPs are persisted.

## DHCP Behavior

The DHCP flow is:

1. Client sends DISCOVER.
2. MYLan offers an address from the configured pool.
3. Client sends REQUEST.
4. MYLan ACKs the lease if the request is valid.

MYLan includes the following DHCP options in replies:

- 1: Subnet mask
- 3: Router/gateway
- 6: DNS server
- 28: Broadcast address
- 51: Lease time
- 53: DHCP message type
- 54: DHCP server identifier

## Troubleshooting

`Access denied binding to port 67`

Run as Administrator on Windows or with `sudo` on macOS/Linux.

`UDP port 67 is already in use`

Stop any other DHCP server, Internet Sharing service, or previous MYLan instance.

Clients do not receive addresses

- Confirm the selected adapter is connected to the isolated network.
- Confirm the pool is in the same subnet as the server IP.
- Confirm the firewall allows inbound UDP/67.
- Check that another DHCP server is not already active on the same link.

Adapter settings did not restore

Restore is best-effort. If the OS rejects the restore command, manually set the adapter back to DHCP or its previous static configuration.
