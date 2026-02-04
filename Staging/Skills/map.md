---
name: map
description: Create interactive maps with PowerShell.Map module
allowed-tools: mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, WebSearch
---

LANGUAGE:
Communicate with users in the user's native language

Create interactive map for "$ARGUMENTS" using PowerShell.Map module.
Parse arguments: First is map_theme (required), second is target_area (optional)

**IMPORTANT: If both theme and target area are provided, skip user confirmation and start map creation.**

WORKFLOW:
1. Run start_powershell_console
2. If PowerShell.Map not installed, run `Install-Module PowerShell.Map -Force`. Otherwise, mention: "Run 'Update-Module PowerShell.Map -Force' if you haven't recently"
3. If theme/area incomplete, confirm with user first
4. Research locations thoroughly (use web search for detailed practical information)
5. **CRITICAL - Variable Scope**: Store location data in `$global:` scope
   ```powershell
   $global:mapLocations = @(
       @{ Location = "....."; Label = "....."; Color = "....."; Description = "....." }
   )
   ```
6. Display map using Show-OpenStreetMap with **3D enabled by default**
   ```powershell
   Show-OpenStreetMap -Locations $global:mapLocations -Enable3D -Zoom 12 -Pitch 60
   ```
7. **CRITICAL - Validate coordinates after display:**
   - Check latitude/longitude in results
   - If outliers exist (distance > 0.5° from median): inform user, remove, re-display
8. Start automated tour (-Enable3D -Zoom 16 -Pitch 60 -Duration 8 -PauseTime 7)
   After tour completes, re-display all spots with camera reset (Pitch 0, Bearing 0)

## PowerShell.Map Commands

### Basic Usage:
```powershell
$locations = @(
    @{
        Location = "Tokyo Tower"
        Label = "🗼 Tokyo Tower"
        Color = "red"
        Description = "🗼 Tokyo Tower`nHeight: 332.9m`nBuilt: 1958`nEntry: ¥1,200"
    }
)
Show-OpenStreetMap -Locations $locations -Enable3D -Zoom 12 -Pitch 60
```

### Parameters:
- **-Locations**: Array of hashtables with Location, Label, Color, Description
- **-Enable3D**: Show 3D buildings/terrain (recommended)
- **-Disable3D**: Force 2D flat view
- **-Zoom**: 1-19 (default=13)
- **-Pitch**: 0-85° (0=top-down, 60=3D view)
- **-Bearing**: 0-360° (0=North, 90=East)
- **-Duration**: 0.0-10.0s animation

### Other Commands:
```powershell
# Route display
Show-OpenStreetMapRoute -From "Tokyo" -To "Osaka" -Color "#ff0000" -Width 6

# Automated tour
Start-OpenStreetMapTour -Locations $tourStops -Enable3D -Zoom 16 -Pitch 60 -Duration 8 -PauseTime 7
```

### Colors:
red, blue, green, orange, violet, yellow, grey, black, gold

## Description Best Practices

Include in descriptions:
- Emoji identifier
- Key facts (height, date, capacity, rating)
- Practical info (entry fee, hours, access)
- Tips (best time, insider knowledge)

Format: Use backtick-n (`n) for line breaks