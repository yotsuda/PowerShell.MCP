Start RAPID dictation training for {{request}} using PowerShell.MCP.

Parse the request for: target language, sentence length (Short/Medium/Long/VeryLong), speech speed (Slow/Normal/Fast/VeryFast), topic, show translation (on/off). Use defaults if not specified.

WORKFLOW:
1. Start PowerShell → Minimize + Initialize Speech:
   Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class Win32{ [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr hWnd,int nCmdShow);[DllImport("kernel32.dll")]public static extern IntPtr GetConsoleWindow();}'; [Win32]::ShowWindow([Win32]::GetConsoleWindow(),2); Add-Type -AssemblyName System.Speech; $global:speech = New-Object System.Speech.Synthesis.SpeechSynthesizer; $speech.Rate = 0
2. Check available voices and set for target language:
   $speech.GetInstalledVoices() | ForEach-Object { Write-Host "Voice: $($_.VoiceInfo.Name) - Language: $($_.VoiceInfo.Culture)" }
3. Show guidance (user's native language)
4. Generate sentence → Play twice at specified speed
   (Slow=-2, Normal=0, Fast=+2, VeryFast=+4)
5. User answers in this chat (not PowerShell)
6. Before each new question, replay the previous sentence once, then play the new one twice.
7. Show brief feedback → Continue immediately
8. Say "stop" to end → Restore console: [Win32]::ShowWindow([Win32]::GetConsoleWindow(),9)

SENTENCE LENGTHS: Short=3-5, Medium=6-8, Long=9-12, VeryLong=13-15 words
SPEECH RATE MAPPING: Slow=-2, Normal=0, Fast=+2, VeryFast=+4

GUIDANCE TEMPLATE:
🎯 DICTATION TRAINING

* Audio from minimized PowerShell
* Answer here in chat
* Commands: stop / repeat / faster / slower / longer / shorter / info / skip / back
* Say topic word (e.g., "airport") to change theme
* Say "translation on/off" to toggle translation

QUESTION FORMAT:
**Question [X]** ([correct]/[total] correct)

AUDIO COMMAND:
$speech.Rate=[rate]; $speech.Speak("[previous_sentence]"); Start-Sleep -Seconds 1; $speech.Speak("[sentence]"); $speech.Speak("[sentence]")

FEEDBACK FORMAT:
✅/❌ ([correct] / [total]) | Accuracy: [score] % | Tip: [brief tip]

RULES:
- Guidance / feedback in native language, dictation in target language
- Replay previous sentence before next question
- Don't show answers before user responds
- Accept phonetic / spelling variations
- Calculate (correct words / total) × 100 %
- One short tip per feedback
- Keep pace fast and natural
- Use everyday sentences for topic
- Restore console on stop: [Win32]::ShowWindow([Win32]::GetConsoleWindow(), 9)