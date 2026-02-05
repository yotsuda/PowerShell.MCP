using System.Reflection;

namespace PowerShell.MCP.Proxy.Prompts;

/// <summary>
/// Loads prompt templates from embedded resources.
/// </summary>
public static class PromptTemplateLoader
{
    private static readonly Assembly Assembly = typeof(PromptTemplateLoader).Assembly;

    /// <summary>
    /// Loads a prompt template from embedded resources and replaces the {{request}} placeholder.
    /// </summary>
    /// <param name="templateName">Template file name (e.g., "learn.md")</param>
    /// <param name="request">The user's request to substitute for {{request}}</param>
    /// <returns>The processed prompt text</returns>
    public static string Load(string templateName, string? request = null)
    {
        var resourceName = $"PowerShell.MCP.Proxy.Prompts.Templates.{templateName}";
        
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Template not found: {templateName}");
        
        using var reader = new StreamReader(stream);
        var template = reader.ReadToEnd();
        
        if (!string.IsNullOrEmpty(request))
        {
            template = template.Replace("{{request}}", request);
        }
        
        return template;
    }
}