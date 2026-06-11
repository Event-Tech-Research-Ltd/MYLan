# MYLan cross-platform build notes

This version is set up as a .NET 8 Avalonia desktop app.

## Windows x64

```powershell
dotnet publish .\MYLan\MYLan.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\release\win-x64
```

Output:

```text
release\win-x64\MYLan.exe
```

Run as Administrator because MYLan binds to DHCP UDP/67 and changes adapter IP settings.

## macOS Intel

```bash
dotnet publish ./MYLan/MYLan.csproj -c Release -r osx-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./release/osx-x64
chmod +x ./release/osx-x64/MYLan
sudo ./release/osx-x64/MYLan
```

## macOS Apple Silicon

```bash
dotnet publish ./MYLan/MYLan.csproj -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./release/osx-arm64
chmod +x ./release/osx-arm64/MYLan
sudo ./release/osx-arm64/MYLan
```

## Important macOS notes

For proper public Mac distribution, build a `.app` bundle, code-sign it, notarise it with Apple, and distribute it in a `.dmg` or `.pkg`.

For field/internal testing, the raw `MYLan` executable can be tested from Terminal, but macOS security may require manual approval.

## Why sudo/admin is required

DHCP server mode requires binding to UDP port 67, which is privileged on Unix-like systems, and the app modifies network adapter configuration. Windows requires Administrator; macOS/Linux require root/sudo.
