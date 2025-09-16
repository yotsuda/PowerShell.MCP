$allDrives = Get-PSDrive | ForEach-Object {
    $driveName = [string]$_.Name + ':'
    $providerName = [string]$_.Provider.Name

    $currentPath = ""
    if ($_.CurrentLocation) {
        $currentPath = [string]$_.CurrentLocation
    }

    if ([string]::IsNullOrEmpty($currentPath)) {
        $currentPath = "\"
    } else {
        if (-not $currentPath.StartsWith("\")) {
            $currentPath = "\" + $currentPath
        }
    }

    [PSCustomObject]@{
        provider = $providerName
        drive = $driveName
        currentPath = $currentPath
    }
}

$allDrives | Sort-Object provider, drive | ConvertTo-Json -Depth 2
