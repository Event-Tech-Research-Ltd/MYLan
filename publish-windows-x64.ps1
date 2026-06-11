# Build MYLan standalone Windows x64 release
# Run from the repository root in PowerShell.
dotnet publish .\MYLan\MYLan.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\release\win-x64
