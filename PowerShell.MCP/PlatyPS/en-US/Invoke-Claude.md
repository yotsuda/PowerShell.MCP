---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Invoke-Claude
---

# Invoke-Claude

## SYNOPSIS

Sends a prompt to the Anthropic Claude API and returns the response with real-time streaming.

## SYNTAX

### __AllParameterSets

```
Invoke-Claude [-Prompt] <string> [[-SystemPrompt] <string>] [-Model <string>] [-MaxTokens <int>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Sends a prompt to the Anthropic Claude API using SSE (Server-Sent Events) streaming. Tokens are displayed on the console in real time as they arrive. The full response is returned as an AIResponse object, which behaves like a string in most contexts.

Requires the `ANTHROPIC_API_KEY` environment variable to be set with a valid Anthropic API key.

Friendly model aliases are supported: Opus4, Sonnet4, Haiku4 (and shorter forms Opus, Sonnet, Haiku). Full model IDs (e.g., claude-sonnet-4-20250514) are also accepted. Default model is Sonnet 4.

## EXAMPLES

### Example 1: Basic prompt

```powershell
Invoke-Claude "Explain PowerShell pipelines in one paragraph."
```

Sends the prompt to Claude Sonnet (default model) and streams the response to the console.

### Example 2: Capture result in a variable

```powershell
$result = Invoke-Claude "What is 2+2?"
$result.Text    # "4"
"$result"       # "4" (implicit string conversion)
```

The AIResponse object supports `.Text` property access and implicit string conversion.

### Example 3: Use a specific model

```powershell
Invoke-Claude -Model Opus4 "Write a detailed analysis of this error: $errorMsg"
Invoke-Claude -Model Haiku4 "Translate to Japanese: Hello"
```

Use `-Model` with friendly aliases (Opus4, Sonnet4, Haiku4) or full model IDs.

### Example 4: Pipeline input

```powershell
Get-Content error.log | Invoke-Claude "Summarize the errors in this log."
```

Content piped into Invoke-Claude is joined with newlines and prepended to the prompt.

### Example 5: System prompt

```powershell
Invoke-Claude "List running services" -SystemPrompt "You are a Windows system administrator. Be concise."
```

The system prompt guides the AI's behavior and response style.

### Example 6: Pipe output to another command

```powershell
Invoke-Claude "Generate a CSV of 5 sample users" | Set-Content users.csv
```

When piped, streaming display is suppressed and the output goes directly to the pipeline.

## PARAMETERS

### -MaxTokens

Maximum number of tokens in the AI response. Default is 4096.

```yaml
Type: System.Int32
DefaultValue: '4096'
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Model

Model name or friendly alias. Aliases: Opus4, Opus, Sonnet4, Sonnet, Haiku4, Haiku. Full model IDs are also accepted. Default is claude-sonnet-4-20250514 (Sonnet 4).

```yaml
Type: System.String
DefaultValue: 'claude-sonnet-4-20250514'
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Prompt

The user prompt to send to the AI. Accepts pipeline input. Multiple pipeline strings are joined with newlines.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -SystemPrompt

Optional system prompt to guide the AI's behavior and response style.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 1
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.String

Prompt text. Multiple strings from the pipeline are joined with newlines.

## OUTPUTS

### PowerShell.MCP.Cmdlets.AIResponse

An object containing the AI response text. Has a `.Text` property and supports implicit conversion to string via `ToString()`.

## NOTES

- Requires `ANTHROPIC_API_KEY` environment variable.
- Uses Anthropic Messages API (https://api.anthropic.com/v1/messages) with SSE streaming.
- API version: 2023-06-01 (stable, unchanged since 2023).
- Streaming tokens are displayed via Host.UI.Write, which does not interfere with pipeline output.

## RELATED LINKS

- [Invoke-GPT](Invoke-GPT.md)
- [Anthropic API Documentation](https://docs.anthropic.com/en/api/messages)
