$current = Get-Location
$allDrives = Get-PSDrive | ForEach-Object {
    $driveName = [string]$_.Name + ':'
    $providerName = [string]$_.Provider.Name
    if ($_.CurrentLocation) {
        $currentPath = [string]$_.CurrentLocation
    } else {
        $currentPath = [string]$_.Root
    }
    [PSCustomObject]@{
        drive = $driveName
        currentPath = $currentPath
        provider = $providerName
        isCurrent = ([string]$_.Name -eq [string]$current.Drive)
    }
}

$currentDriveName = [string]$current.Drive + ':'
$currentPath = [string]$current.Path
$currentProvider = [string]$current.Provider.Name

$otherDrives = $allDrives | Where-Object { -not $_.isCurrent }

[PSCustomObject]@{
    currentLocation = [PSCustomObject]@{
        drive = $currentDriveName
        currentPath = $currentPath
        provider = $currentProvider
    }
    otherDriveLocations = @($otherDrives | Select-Object drive, currentPath, provider)
} | ConvertTo-Json -Depth 3
