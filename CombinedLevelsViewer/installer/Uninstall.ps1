<#
Removes Clive for the current Windows user: the installed files, the File Explorer
right-click command and the "Open with" entry. Run with -WhatIf to see what would be done.
An "Open With" entry added by hand in Visual Studio has to be removed there (Open With > select it > Remove).
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
	[string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\Clive')
)

$ErrorActionPreference = 'Stop'
$exeName = 'Clive.exe'

if (-not $WhatIfPreference -and (Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($exeName)) -ErrorAction SilentlyContinue)) {
	throw 'Clive is running. Close it and run the uninstaller again.'
}

$keys = @(
	'HKCU:\Software\Classes\SystemFileAssociations\.lvl\shell\Clive',
	'HKCU:\Software\Classes\SystemFileAssociations\.tcg\shell\Clive',
	"HKCU:\Software\Classes\Applications\$exeName"
)
foreach ($key in $keys) {
	if ((Test-Path $key) -and $PSCmdlet.ShouldProcess($key, 'Remove registry key')) {
		Remove-Item $key -Recurse -Force
	}
}

# The second folder holds the viewer's browser cache.
foreach ($folder in $InstallDirectory, (Join-Path $env:LOCALAPPDATA 'Clive')) {
	if ((Test-Path $folder) -and $PSCmdlet.ShouldProcess($folder, 'Remove folder')) {
		Remove-Item $folder -Recurse -Force
	}
}

if (-not $WhatIfPreference) { Write-Host 'Clive removed.' }
