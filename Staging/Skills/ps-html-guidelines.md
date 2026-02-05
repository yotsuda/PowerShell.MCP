---
name: ps-html-guidelines
description: Technical guidelines for generating HTML reports with Chart.js
allowed-tools: mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location
---

## HTML Report Generation - Technical Constraints

**USAGE:** Apply these technical guidelines when generating HTML files as specified in task instructions.

**CRITICAL - Variable Scope:** Use `$script:` for ALL variables shared across invoke_expression calls.

**CRITICAL - Chart.js Data:** Extract arrays using ForEach-Object (%), NOT .Count.
```powershell
# ❌ ($data | Select -First 10).Count  # Returns number, not array
# ✅ Correct:
$script:labels = ($data | Select -First 10 | % { $_.Name }) | ConvertTo-Json -Compress -AsArray
$script:values = ($data | Select -First 10 | % { $_.Count }) | ConvertTo-Json -Compress -AsArray
```

**CRITICAL - Data Validation:** Verify Chart.js data format with Write-Host.
```powershell
Write-Host "Labels: $script:labels"  # Must show ["value"] not "value"
Write-Host "Values: $script:values"  # Must show [10] not 10
```

**CRITICAL - CSS Color Specification:** ALWAYS set BOTH background AND color explicitly for every element.
```css
/* ❌ BAD: Inherits parent's white color */
.parent { background: gradient; color: white; }
.child { background: white; } /* white on white - unreadable! */

/* ✅ GOOD: Explicitly overrides */
.parent { background: gradient; color: white; }
.child { background: white; color: #333; }
.table-container { background: white; color: #333; } /* Always explicit */
```
Never rely on color inheritance. Light bg → dark text, dark bg → light text.

**Design:**
- Display analysis target path at the top of the report (in title or header section)
- Chart.js: https://cdn.jsdelivr.net/npm/chart.js
- Choose the most appropriate chart type based on the analysis results (e.g., Bar, Line, Pie, Scatter, Histogram, Box Plot, Stacked Bar, Radar).
- Use gradients in CSS and Chart.js (ctx.createLinearGradient)
- Use cohesive color palette with visual harmony - diversify colors across charts
- Responsive, print-friendly, with "Back to Top" button

**Output:**
If the analysis target is a file, save it in the same folder as that file.
If the analysis target is a folder, save it inside that folder.
The report file name should be as follows:
$reportPath = "$sourceFolder\$contentName_AnalysisReport_$(Get-Date -Format 'yyyyMMdd_HHmmss').html"

# Save HTML to $reportPath, then:
Start-Process $reportPath
