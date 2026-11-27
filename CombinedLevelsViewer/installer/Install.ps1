<#
Installs Clive for the current Windows user. No administrator rights are needed.

- Copies the viewer to %LOCALAPPDATA%\Programs\Clive.
- Adds "Open in Clive" to the File Explorer right-click menu of .lvl and .tcg files.
- Lists the viewer in the File Explorer "Open with" dialog for .lvl and .tcg files.

It does not change which program opens these files by default, and it does not change Visual Studio.
Run with -WhatIf to see what would be done without doing it.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
	[string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\Clive')
)

$ErrorActionPreference = 'Stop'
$exeName = 'Clive.exe'
$source = Join-Path $PSScriptRoot 'app'

if (-not (Test-Path (Join-Path $source $exeName))) {
	throw "The 'app' folder next to this script does not contain $exeName. Extract the whole package before installing."
}
if (-not $WhatIfPreference -and (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue)) {
	throw 'Clive is running. Close it and run the installer again.'
}

if ($PSCmdlet.ShouldProcess($InstallDirectory, 'Copy the viewer')) {
	if (Test-Path $InstallDirectory) { Remove-Item $InstallDirectory -Recurse -Force }
	New-Item -ItemType Directory -Path (Split-Path $InstallDirectory -Parent) -Force | Out-Null
	Copy-Item $source $InstallDirectory -Recurse
}

$exe = Join-Path $InstallDirectory $exeName
$command = '"' + $exe + '" "%1"'

foreach ($extension in '.lvl', '.tcg') {
	$verb = "HKCU:\Software\Classes\SystemFileAssociations\$extension\shell\Clive"
	if ($PSCmdlet.ShouldProcess($verb, 'Add the File Explorer right-click command')) {
		New-Item -Path "$verb\command" -Force | Out-Null
		Set-Item -Path $verb -Value 'Open in Clive'
		Set-ItemProperty -Path $verb -Name 'Icon' -Value $exe
		Set-Item -Path "$verb\command" -Value $command
	}
}

$application = "HKCU:\Software\Classes\Applications\$exeName"
if ($PSCmdlet.ShouldProcess($application, 'List the viewer in the File Explorer "Open with" dialog')) {
	New-Item -Path "$application\shell\open\command" -Force | Out-Null
	Set-Item -Path "$application\shell\open\command" -Value $command
	Set-ItemProperty -Path $application -Name 'FriendlyAppName' -Value 'Clive'
	New-Item -Path "$application\SupportedTypes" -Force | Out-Null
	Set-ItemProperty -Path "$application\SupportedTypes" -Name '.lvl' -Value ''
	Set-ItemProperty -Path "$application\SupportedTypes" -Name '.tcg' -Value ''
}

$version = (Get-Item (Join-Path $source $exeName)).VersionInfo.ProductVersion -replace '\+.*$', ''

Write-Host ''
Write-Host "Clive $version installed to:"
Write-Host "  $exe"
Write-Host ''

# The viewer shows its tables in the grid that ships with the Torch extension, and needs WebView2.
$torch = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Torch') -Directory -ErrorAction SilentlyContinue |
	Where-Object { Test-Path (Join-Path $_.FullName 'Web\Html\BaseComponents\Bootstrapper.js') }
if (-not $torch) {
	Write-Warning 'The Torch extension was not found under %LOCALAPPDATA%\Torch. Install Torch in Visual Studio; the viewer cannot show its tables without it.'
}
$webView2Id = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
$webView2 = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients', 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients', 'HKCU:\Software\Microsoft\EdgeUpdate\Clients' |
	Where-Object { Test-Path (Join-Path $_ $webView2Id) }
if (-not $webView2) {
	Write-Warning 'The Microsoft Edge WebView2 Runtime was not found. Install it from https://developer.microsoft.com/microsoft-edge/webview2/ if the viewer window stays empty.'
}

Write-Host 'File Explorer: right-click LevelsSequences.lvl or BaseLevels.tcg and choose "Open in Clive".'
Write-Host '               On Windows 11 the command is under "Show more options".'
Write-Host 'Visual Studio: follow "Add the viewer to Open With in Visual Studio" in README.txt, using the path above.'
