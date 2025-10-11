using System.Resources;
using System.Reflection;

namespace PowerShell.MCP.Proxy.Attributes;

/// <summary>
/// Provides localized names for MCP prompts using .resx resource files.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class LocalizedNameAttribute : Attribute
{
    private readonly string _resourceKey;
    private static ResourceManager? _resourceManager;
    private string? _resolvedName;

    /// <summary>
    /// Creates a localized name attribute that loads text from resource files.
    /// </summary>
    /// <param name="resourceKey">Key in the resource file (e.g., "Prompt_SoftwareDevelopment_Name")</param>
    public LocalizedNameAttribute(string resourceKey)
    {
        _resourceKey = resourceKey;
    }

    public string? Name
    {
        get
        {
            _resolvedName ??= ResolveName();
            return _resolvedName;
        }
    }

    private string? ResolveName()
    {
        try
        {
            // Initialize ResourceManager lazily
            if (_resourceManager == null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                _resourceManager = new ResourceManager(
                    "PowerShell.MCP.Proxy.Resources.PromptDescriptions",
                    assembly);
            }

            // Get localized string based on current UI culture
            var localizedString = _resourceManager.GetString(_resourceKey);
            
            // Return null if not found - this will use the default prompt name
            return string.IsNullOrEmpty(localizedString) ? null : localizedString;
        }
        catch
        {
            // If resource loading fails, return null to use default prompt name
            return null;
        }
    }
}