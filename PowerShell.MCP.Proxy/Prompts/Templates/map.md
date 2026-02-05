LANGUAGE:
Communicate with users in the user's native language

Create interactive map for {{request}} using PowerShell.Map module.

**IMPORTANT: If both theme and target area are clear from the request, skip user confirmation and immediately start map creation.**

WORKFLOW:
1. Run start_powershell_console
2. If PowerShell.Map not installed, run `Install-Module PowerShell.Map -Force`. Otherwise, briefly mention: "Run 'Update-Module PowerShell.Map -Force' if you haven't recently" (then proceed immediately)
3. If theme/area incomplete, confirm with user first
4. Research locations thoroughly (use web search for detailed practical information)
5. **CRITICAL - Variable Scope**: Store location data in `$global:` scope for reuse across multiple invoke_expression calls
   ```powershell
   $global:mapLocations = @(
       @{ Location = "....."; Label = "....."; Color = "....."; Description = "....." }
   )
   ```
   Create rich location data with Labels, Colors, and **detailed Descriptions**
6. Display map using Show-OpenStreetMap with **3D enabled by default** (unless flat terrain)
   ```powershell
   Show-OpenStreetMap -Locations $global:mapLocations -Enable3D -Zoom 12 -Pitch 60
   ```
7. **CRITICAL - Validate coordinates after display:**
   - Check latitude/longitude in results
   - If outliers exist (distance > 0.5° from median): inform user, remove from `$global:mapLocations`, re-display
8. Start automated tour (-Enable3D -Zoom 16 -Pitch 60 -Duration 8 -PauseTime 7) → **After tour completes, re-display all spots with camera reset (Pitch 0, Bearing 0)**
   ```powershell
   Show-OpenStreetMap -Locations $global:mapLocations -Pitch 0 -Bearing 0
   ```

## PowerShell.Map Commands

### Basic Usage:
```powershell
$locations = @(
    @{
        Location = "Tokyo Tower"
        Label = "🗼 Tokyo Tower"
        Color = "red"
        Description = "🗼 Tokyo Tower`nHeight: 332.9m`nBuilt: 1958`nEntry: ¥1,200`nHours: 9:00-23:00`nBest: Sunset views"
    }
)
Show-OpenStreetMap -Locations $locations -Enable3D -Zoom 12 -Pitch 60
```

### Parameters:
- **-Locations**: Array of hashtables with Location, Label, Color, Description
- **-Enable3D**: Show 3D buildings/terrain (recommended for cities/mountains)
- **-Disable3D**: Force 2D flat view
- **-Zoom**: 1-19 (default=13)
- **-Pitch**: 0-85° (0=top-down, 60=3D view)
- **-Bearing**: 0-360° (0=North, 90=East)
- **-Duration**: 0.0-10.0s animation (0=instant)

**IMPORTANT:** If Pitch/Bearing/3D are NOT specified, the map maintains its current state (user's last camera position/mode). Only specify these when you want to change or reset the view.

### Other Commands:
```powershell
# Route display
Show-OpenStreetMapRoute -From "Tokyo" -To "Osaka" -Color "#ff0000" -Width 6

# Automated tour with descriptions (CRITICAL for guided tours)
$tourStops = @(
    @{ Location = "Tokyo Tower"; Description = "🗼 Tokyo Tower`nHeight: 332.9m`nBuilt: 1958`nBest view: Sunset" }
    @{ Location = "Mount Fuji"; Description = "🗻 Mt. Fuji`nElevation: 3,776m`nUNESCO World Heritage`nBest: Early morning" }
    @{ Location = "Kyoto"; Description = "⛩️ Kyoto`n2000+ temples`nFormer capital 794-1868" }
)
Start-OpenStreetMapTour -Locations $tourStops -Enable3D -Zoom 16 -Pitch 60 -Duration 8 -PauseTime 7
```

### Colors:
red, blue, green, orange, violet, yellow, grey, black, gold

## Description Best Practices

**CRITICAL:** Descriptions appear on marker click. Make them informative!

Include:
- Emoji identifier
- Key facts (height, date, capacity, rating)
- Practical info (entry fee, hours, access)
- Tips (best time, insider knowledge)

Format: Use backtick-n (`n) for line breaks, keep lines 40-60 chars

Example:
```
🗼 Eiffel Tower`nHeight: 330m (1,083 ft)`nBuilt: 1889`nEntry: €28 summit, €18 2nd floor`nHours: 9:00-00:45`nBest: Sunset or night illumination`nAccess: Trocadéro Metro 10 min
```

## Map Theme Examples

- **Tourist spots**: Color by type (museums=blue, monuments=red, parks=green)
- **Restaurants**: Color by cuisine type, include ratings/prices
- **Natural landmarks**: Color by feature type, include elevation/access
- **Historical sites**: Color by period, include UNESCO status/dates
- **Viewpoints**: Color by view type, include best photo times

## Notes

- Map opens at http://localhost:8765/
- Always validate coordinates for geographic accuracy
- Use web search for current information (hours, prices)
- 3D recommended when geography/architecture matters
- When showing new markers to existing map, omit Pitch/Bearing/3D to preserve user's view