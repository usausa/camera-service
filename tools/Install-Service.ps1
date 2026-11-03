param(
    # Folder of the published service (CameraService.Host.exe and appsettings.json)
    [Parameter(Mandatory)]
    [string]$Path,
    [string]$Name = 'CameraService',
    [string]$DisplayName = 'Camera Service',
    [ValidateSet('Automatic', 'Manual')]
    [string]$StartupType = 'Automatic',
    # Opens the HTTP port in Windows Firewall for the other PCs of the network (0: not opened)
    [int]$FirewallPort = 0
)

# Registers and starts the camera service (run as administrator).
# The service runs as LocalSystem, so the camera must be allowed for the device (Settings > Privacy & security > Camera).
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run as administrator.'
}

$exe = Join-Path (Resolve-Path $Path) 'CameraService.Host.exe'
if (-not (Test-Path $exe)) {
    throw "Not found: $exe"
}

New-Service -Name $Name -BinaryPathName "`"$exe`"" -DisplayName $DisplayName -Description 'Camera streaming and snapshots' -StartupType $StartupType | Out-Null
# Restarted after a failure (1 minute later, the count is reset after a day)
sc.exe failure $Name reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
if ($FirewallPort -gt 0) {
    New-NetFirewallRule -DisplayName $DisplayName -Direction Inbound -Protocol TCP -LocalPort $FirewallPort -Action Allow | Out-Null
}

Start-Service -Name $Name
Get-Service -Name $Name
