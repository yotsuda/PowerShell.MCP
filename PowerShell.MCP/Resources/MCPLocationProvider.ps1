# Build location information structure
$locationInfo = [ordered]@{
    # System information
    system = [ordered]@{
        os = [System.Environment]::OSVersion.VersionString
        powershell_version = $PSVersionTable.PSVersion.ToString()
        host_name = [System.Net.Dns]::GetHostName()
        current_user = if ($IsWindows) { [Security.Principal.WindowsIdentity]::GetCurrent().Name } else { [Environment]::UserName }
        is_elevated = if ($IsWindows) { ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } else { (id -u) -eq 0 }
        culture = [System.Globalization.CultureInfo]::CurrentCulture.Name
        timezone = [System.TimeZoneInfo]::Local.Id
        timezone_offset = & {
            $offset = [System.TimeZoneInfo]::Local.BaseUtcOffset
            $sign = if ($offset.TotalHours -ge 0) { "+" } else { "-" }
            "{0}{1:D2}:{2:D2}" -f $sign, [Math]::Abs($offset.Hours), [Math]::Abs($offset.Minutes)
        }
        execution_policy = (Get-ExecutionPolicy).ToString()
    }
    # Available drives information
    available_drives = @(
        Get-PSDrive | ForEach-Object {
            $currentPath = if ($_.CurrentLocation) { 
                $path = [string]$_.CurrentLocation
                if (-not $path.StartsWith("\")) { "\" + $path } else { $path }
            } else { 
                "\" 
            }

            # Build drive object with only non-empty properties
            $driveObj = [ordered]@{
                name = [string]$_.Name
                provider = [string]$_.Provider.Name
            }

            # Add root only if not empty
            if ($_.Root -and [string]$_.Root -ne "") {
                $driveObj.root = [string]$_.Root
            }

            $driveObj.current_path = $currentPath

            # Add description only if not empty
            if ($_.Description -and [string]$_.Description -ne "") {
                $driveObj.description = [string]$_.Description
            }

            [PSCustomObject]$driveObj
        } | Sort-Object provider, name
    )
}

# Output as JSON
$locationInfo | ConvertTo-Json -Depth 3
