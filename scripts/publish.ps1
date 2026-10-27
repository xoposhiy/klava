# Builds dist\Klava.exe with its DLLs (about 1 MB). Needs the .NET 10 Desktop Runtime.
# PublishSingleFile is not used: with this SDK it bundles the whole runtime (110 MB).
# -Zip also writes artifacts\Klava-<version>-win-x64.zip for a release.
# -Restart stops the running Klava before the publish (it locks the files in dist) and starts
# the new dist\Klava.exe after it.
param([switch]$Zip, [switch]$Restart)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = "$root\dist\Klava.exe"
dotnet test "$root" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

if ($Restart) {
    $running = Get-Process Klava -ErrorAction SilentlyContinue
    if ($running) {
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 10
        Write-Host "Stopped Klava ($($running.Id -join ', '))"
    }
}

dotnet publish "$root\src\Klava.App" -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -o "$root\dist" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Host "Published $exe"

if ($Restart) {
    Start-Process $exe
    Write-Host "Started $exe"
}

if ($Zip) {
    $version = ([xml](Get-Content "$root\Directory.Build.props")).Project.PropertyGroup.Version
    New-Item -ItemType Directory -Force "$root\artifacts" | Out-Null
    $zipPath = "$root\artifacts\Klava-$version-win-x64.zip"
    Compress-Archive -Path "$root\dist\*", "$root\LICENSE", "$root\README.md" -DestinationPath $zipPath -Force
    Write-Host "Packed $zipPath"
}
