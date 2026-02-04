---
name: html-guidelines
description: Technical guidelines for generating HTML reports with Chart.js
allowed-tools: mcp__PowerShell__invoke_expression
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
```
Never rely on color inheritance. Light bg → dark text, dark bg → light text.

**Design:**
- Display analysis target path at the top of the report
- Chart.js: https://cdn.jsdelivr.net/npm/chart.js
- Choose appropriate chart types (Bar, Line, Pie, Scatter, etc.)
- Use gradients in CSS and Chart.js
- Responsive, print-friendly, with "Back to Top" button

**Output:**
Save report in same folder as analysis target:
$reportPath = "$sourceFolder\$contentName_AnalysisReport_$(Get-Date -Format 'yyyyMMdd_HHmmss').html"

# Save HTML to $reportPath, then:
Start-Process $reportPath