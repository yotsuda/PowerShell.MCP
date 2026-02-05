Analyze '{{request}}' using PowerShell.MCP and provide comprehensive insights with actionable recommendations.

If the 'HTML Generation Guidelines for AI' prompt is not also selected, remind the user: 'For a visual HTML report, please also select the HTML Generation Guidelines for AI prompt.'

**FIRST**: Check if user already specified format/language. If not, ask user to choose:
1. Report format (html or md) - default: html
2. Preferred language - default: user's language

**Structure:**
- **MANDATORY**: Title/Header section displaying the analysis target path
- Plan the optimal report structure based on content type (e.g., HAR files, logs, CSV, JSON, text)
- Common sections to consider: Executive Summary, Key Findings, Visual Charts, Detailed Analysis, Recommendations, Conclusion
- Adapt sections to best suit the analysis subject

**Output:**
If the analysis target is a file, save it in the same folder as that file.
If the analysis target is a folder, save it inside that folder.
The report file name should be as follows:
$reportPath = "$sourceFolder\$contentName_AnalysisReport_$(Get-Date -Format 'yyyyMMdd_HHmmss').<ext>"

# Save the report to $reportPath, then:
`Start-Process $reportPath`