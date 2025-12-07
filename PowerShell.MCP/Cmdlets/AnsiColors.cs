namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ANSI escape sequences for terminal colors and formatting.
/// Centralized definitions for consistent styling across all cmdlets.
/// </summary>
internal static class AnsiColors
{
    private const char Esc = (char)27;

    // Basic formatting
    public static readonly string Reset = $"{Esc}[0m";
    public static readonly string Bold = $"{Esc}[1m";

    // Foreground colors
    public static readonly string Red = $"{Esc}[31m";
    public static readonly string Green = $"{Esc}[32m";
    public static readonly string Yellow = $"{Esc}[33m";
    public static readonly string Cyan = $"{Esc}[36m";
    public static readonly string BrightWhite = $"{Esc}[97m";

    // Combined styles (for match highlighting)
    public static readonly string RedOnYellow = $"{Esc}[31;43m";      // Red text on yellow background
    public static readonly string RedOnDefault = $"{Esc}[31;49m";     // Red text, reset background

    // Helper methods for common patterns
    public static string Header(string path) => $"{Bold}==> {path} <=={Reset}";
    public static string Success(string message) => $"{Cyan}{message}{Reset}";
    public static string WhatIf(string message) => $"{Yellow}{message}{Reset}";
    public static string Info(string message) => $"{BrightWhite}{message}{Reset}";
    public static string Inserted(string text) => $"{Green}{text}{Reset}";
    public static string Deleted(string text) => $"{Red}{text}{Reset}";
    public static string Highlight(string text) => $"{Yellow}{text}{Reset}";
}