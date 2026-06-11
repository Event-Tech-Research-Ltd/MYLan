#!/usr/bin/env bash
set -euo pipefail

# Build MYLan standalone macOS releases.
# Run from the repository root on a machine with the .NET SDK installed.

dotnet publish ./MYLan/MYLan.csproj -c Release -r osx-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./release/osx-x64

dotnet publish ./MYLan/MYLan.csproj -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./release/osx-arm64

chmod +x ./release/osx-x64/MYLan || true
chmod +x ./release/osx-arm64/MYLan || true
