# MYLan Cross-Platform Build Notes

MYLan is a .NET 8 Avalonia desktop app.

## Restore and Build

```powershell
dotnet restore .\MYLan\MYLan.csproj
dotnet build .\MYLan\MYLan.csproj -c Release
```

## Windows x64

```powershell
dotnet publish .\MYLan\MYLan.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\release\win-x64
```

Output:

```text
release\win-x64\MYLan.exe
```

Run as Administrator.

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

## Installers

Windows installer:

```powershell
.\build-installer-windows.ps1
```

macOS app bundles and DMGs:

```bash
./publish-macos.sh
./installer/macos/create-macos-app.sh
./installer/macos/create-dmg-macos.sh
```

Mac-ready zip from Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-macos-zip-windows.ps1
```

## Privileges

DHCP server mode requires binding to UDP port 67 and changing adapter IPv4 settings. Windows requires Administrator; macOS/Linux require root.

## Release Signing

For public release, sign the Windows installer and code-sign/notarise/staple the macOS app or DMG.
