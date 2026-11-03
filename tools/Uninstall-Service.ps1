param(
    [string]$Name = 'CameraService',
    [string]$DisplayName = 'Camera Service'
)

# Stops and removes the camera service and its firewall rule (run as administrator)
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run as administrator.'
}

if (Get-Service -Name $Name -ErrorAction SilentlyContinue) {
    Stop-Service -Name $Name
    Remove-Service -Name $Name
}

Get-NetFirewallRule -DisplayName $DisplayName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
