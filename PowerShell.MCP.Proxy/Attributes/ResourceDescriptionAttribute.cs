using System.ComponentModel;
using System.Resources;
using System.Reflection;

namespace PowerShell.MCP.Proxy.Attributes;

/// <summary>
/// Provides localized descriptions for MCP prompt names and parameters using .resx resource files.
/// Create Resources/PromptDescriptions.resx for default (English)
/// and Resources/PromptDescriptions.ja.resx for Japanese translations.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
public class ResourceDescriptionAttribute : DescriptionAttribute
{
    private readonly string _resourceKey;
    private static ResourceManager? _resourceManager;
    private string? _resolvedDescription;

    /// <summary>
    /// Creates a description attribute that loads text from resource files.
    /// </summary>
    /// <param name="resourceKey">Key in the resource file (e.g., "Prompt_AnalyzeContent_Description")</param>
    public ResourceDescriptionAttribute(string resourceKey)
    {
        _resourceKey = resourceKey;
    }

    public override string Description
    {
        get
        {
            _resolvedDescription ??= ResolveDescription();
            return _resolvedDescription;
        }
    }

    private string ResolveDescription()
    {
        try
        {
            // Initialize ResourceManager lazily
            if (_resourceManager == null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                // Uses PromptDescriptions resource file
                _resourceManager = new ResourceManager(
                    "PowerShell.MCP.Proxy.Resources.PromptDescriptions",
                    assembly);
            }

            // Get localized string based on current UI culture
            var localizedString = _resourceManager.GetString(_resourceKey);

            // Return empty string if not found - Description is required
            return localizedString ?? string.Empty;
        }
        catch
        {
            // If resource loading fails, return empty string
            return string.Empty;
        }
    }
}
