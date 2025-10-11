using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using PowerShell.MCP.Proxy.Attributes;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for adding localized prompts to the MCP server.
/// </summary>
public static class LocalizedMcpServerBuilderExtensions
{
    /// <summary>
    /// Adds types marked with McpServerPromptTypeAttribute from the given assembly as prompts to the server,
    /// with support for localized prompt names via LocalizedNameAttribute.
    /// </summary>
    public static IMcpServerBuilder WithLocalizedPromptsFromAssembly(
        this IMcpServerBuilder builder,
        Assembly? promptAssembly = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        try
        {
            promptAssembly ??= Assembly.GetCallingAssembly();

            var promptTypes = promptAssembly.GetTypes()
                .Where(t => t.GetCustomAttribute<McpServerPromptTypeAttribute>() is not null);

            foreach (var promptType in promptTypes)
            {
                var promptMethods = promptType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() is not null);

                foreach (var promptMethod in promptMethods)
                {
                    // Get localized name from LocalizedNameAttribute if present
                    string? localizedName = null;
                    try
                    {
                        if (promptMethod.GetCustomAttribute<LocalizedNameAttribute>() is { } localizedNameAttr)
                        {
                            localizedName = localizedNameAttr.Name;
                        }
                    }
                    catch
                    {
                        // Ignore errors in localization, use default name
                        localizedName = null;
                    }

                    // Get parameter name mappings from LocalizedParameterNameAttribute
                    var parameterNameMappings = new Dictionary<string, string>();
                    foreach (var param in promptMethod.GetParameters())
                    {
                        if (param.GetCustomAttribute<LocalizedParameterNameAttribute>() is { } attr &&
                            attr.Name is { } localizedParamName)
                        {
                            parameterNameMappings[param.Name!] = localizedParamName;
                        }
                    }

                    // Create schema options to add localized parameter titles
                    var schemaCreateOptions = new AIJsonSchemaCreateOptions
                    {
                        TransformSchemaNode = (context, node) =>
                        {
                            // If this is the properties object containing all parameters
                            if (node.GetValueKind() == JsonValueKind.Object && node is JsonObject jsonObject)
                            {
                                // Check each property in the object
                                foreach (var kvp in parameterNameMappings)
                                {
                                    var paramName = kvp.Key;
                                    var localizedTitle = kvp.Value;
                                    
                                    // If this property exists and doesn''t have a title, add it
                                    if (jsonObject.TryGetPropertyValue(paramName, out var paramNode) &&
                                        paramNode is JsonObject paramObject &&
                                        !paramObject.ContainsKey("title"))
                                    {
                                        paramObject["title"] = localizedTitle;
                                    }
                                }
                            }
                            return node;
                        }
                    };

                    // Create options with localized name and schema options
                    var options = new McpServerPromptCreateOptions
                    {
                        Services = null, // Will be set by the factory function
                        SerializerOptions = serializerOptions,
                        Name = localizedName,
                        SchemaCreateOptions = schemaCreateOptions
                    };
                    // Register the prompt
                    if (promptMethod.IsStatic)
                    {
                        builder.Services.AddSingleton<McpServerPrompt>(services =>
                        {
                            options.Services = services;
                            return McpServerPrompt.Create(promptMethod, options: options);
                        });
                    }
                    else
                    {
                        builder.Services.AddSingleton<McpServerPrompt>(services =>
                        {
                            options.Services = services;
                            return McpServerPrompt.Create(
                                promptMethod,
                                r => CreateTarget(r.Services, promptType),
                                options);
                        });
                    }
                }
            }

            return builder;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] WithLocalizedPromptsFromAssembly failed: {ex.Message}");
            Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private static object CreateTarget(IServiceProvider? services, Type targetType)
    {
        if (services is not null && services.GetService(targetType) is { } instance)
        {
            return instance;
        }

        return Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Unable to create an instance of {targetType}.");
    }
}