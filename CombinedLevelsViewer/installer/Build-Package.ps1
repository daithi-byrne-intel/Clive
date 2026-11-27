<#
Builds the downloadable package of Clive:

  dist\Clive-Setup\        folder to hand out (app + Install/Uninstall + README.txt)
  dist\Clive-Setup.zip     the same folder as one file

The viewer is published self-contained for 64-bit Windows, so the people installing it do not need .NET.
Run from anywhere:  powershell -ExecutionPolicy Bypass -File CombinedLevelsViewer\installer\Build-Package.ps1
#>
[CmdletBinding()]
param(
	[string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\CombinedLevelsViewer.TorchTabular\CombinedLevelsViewer.TorchTabular.csproj'
$package = Join-Path $root 'dist\Clive-Setup'
$zip = "$package.zip"

if (Test-Path $package) { Remove-Item $package -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Path $package -Force | Out-Null

dotnet publish $project -c $Configuration -r win-x64 --self-contained true -o (Join-Path $package 'app') --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# Debug symbols are not needed to run the viewer.
Get-ChildItem (Join-Path $package 'app') -Filter '*.pdb' -Recurse | Remove-Item -Force

foreach ($file in 'Install.cmd', 'Install.ps1', 'Uninstall.cmd', 'Uninstall.ps1', 'README.txt') {
	Copy-Item (Join-Path $PSScriptRoot $file) $package
}

Compress-Archive -Path $package -DestinationPath $zip

$size = (Get-ChildItem $package -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Package folder: {0}  ({1:N0} MB, {2} files)" -f $package, ($size / 1MB), (Get-ChildItem $package -Recurse -File).Count)
Write-Host ("Package zip:    {0}  ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
