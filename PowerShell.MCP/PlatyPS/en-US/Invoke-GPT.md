---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Invoke-GPT
---

# Invoke-GPT

## SYNOPSIS

Sends a prompt to the OpenAI API and returns the response with real-time streaming.

## SYNTAX

### __AllParameterSets

```
Invoke-GPT [-Prompt] <string> [[-SystemPrompt] <string>] [-Model <string>] [-MaxTokens <int>]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Sends a prompt to the OpenAI chat completions API using SSE (Server-Sent Events) streaming. Tokens are displayed on the console in real time as they arrive. The full response is returned as an AIResponse object, which behaves like a string in most contexts.

Requires the `OPENAI_API_KEY` environment variable to be set with a valid OpenAI API key.

Friendly model aliases are supported: 4o, GPT4o, 4.1, GPT4.1, o3, o4-mini. Full model IDs (e.g., gpt-4o) are also accepted. Default model is gpt-4o.

## EXAMPLES

### Example 1: Basic prompt

```powershell
Invoke-GPT "Explain PowerShell pipelines in one paragraph."
```

Sends the prompt to GPT-4o (default model) and streams the response to the console.

### Example 2: Capture result in a variable

```powershell
$result = Invoke-GPT "What is 2+2?"
$result.Text    # "4"
"$result"       # "4" (implicit string conversion)
```

The AIResponse object supports `.Text` property access and implicit string conversion.

### Example 3: Use a specific model

```powershell
Invoke-GPT -Model o3 "Write a detailed analysis of this error: $errorMsg"
Invoke-GPT -Model o4-mini "Translate to Japanese: Hello"
```

Use `-Model` with friendly aliases (4o, o3, o4-mini) or full model IDs.

### Example 4: Pipeline input

```powershell
Get-Content error.log | Invoke-GPT "Summarize the errors in this log."
```

Content piped into Invoke-GPT is joined with newlines and prepended to the prompt.

### Example 5: System prompt

```powershell
Invoke-GPT "List running services" -SystemPrompt "You are a Windows system administrator. Be concise."
```

The system prompt guides the AI's behavior and response style.

### Example 6: Compare with Claude

```powershell
$claude = Invoke-Claude "What is the best sorting algorithm?"
$gpt = Invoke-GPT "What is the best sorting algorithm?"
"Claude says: $claude"
"GPT says: $gpt"
```

Use both Invoke-Claude and Invoke-GPT to compare responses from different AI providers.

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

Model name or friendly alias. Aliases: 4o, GPT4o, 4.1, GPT4.1, o3, o4-mini. Full model IDs are also accepted. Default is gpt-4o.

```yaml
Type: System.String
DefaultValue: 'gpt-4o'
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

- Requires `OPENAI_API_KEY` environment variable.
- Uses OpenAI Chat Completions API (https://api.openai.com/v1/chat/completions) with SSE streaming.
- OpenAI API requires prepaid credits (separate from ChatGPT subscription).
- Streaming tokens are displayed via Host.UI.Write, which does not interfere with pipeline output.

## RELATED LINKS

- [Invoke-Claude](Invoke-Claude.md)
- [OpenAI API Documentation](https://platform.openai.com/docs/api-reference/chat)
