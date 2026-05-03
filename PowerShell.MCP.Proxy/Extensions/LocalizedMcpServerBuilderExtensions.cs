using System.Diagnostics.CodeAnalysis;
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
    /// Adds the specified prompt type with support for localized prompt names via LocalizedNameAttribute.
    /// This generic version is trimming-safe.
    /// </summary>
    public static IMcpServerBuilder WithLocalizedPrompts<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods |
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TPromptType>(
        this IMcpServerBuilder builder,
        JsonSerializerOptions? serializerOptions = null)
        where TPromptType : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        try
        {
            var promptType = typeof(TPromptType);
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
                    // Ignore localization errors
                }

                // Build parameter name mappings for localized descriptions
                var parameterNameMappings = new Dictionary<string, string>();
                foreach (var param in promptMethod.GetParameters())
                {
                    var resourceDescAttr = param.GetCustomAttribute<ResourceDescriptionAttribute>();
                    if (resourceDescAttr is not null)
                    {
                        parameterNameMappings[param.Name!] = resourceDescAttr.Description;
                    }
                }

                // Create schema options to inject localized titles for parameters
                var schemaCreateOptions = new AIJsonSchemaCreateOptions
                {
                    TransformSchemaNode = (context, node) =>
                    {
                        if (node.GetValueKind() == JsonValueKind.Object && node is JsonObject jsonObject)
                        {
                            foreach (var kvp in parameterNameMappings)
                            {
                                var paramName = kvp.Key;
                                var localizedTitle = kvp.Value;

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

                var options = new McpServerPromptCreateOptions
                {
                    Services = null,
                    SerializerOptions = serializerOptions,
                    Name = localizedName,
                    SchemaCreateOptions = schemaCreateOptions
                };

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

            return builder;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] WithLocalizedPrompts<{typeof(TPromptType).Name}> failed: {ex.Message}");
            Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private static object CreateTarget(
        IServiceProvider? services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type targetType)
    {
        if (services is not null && services.GetService(targetType) is { } instance)
        {
            return instance;
        }

        return Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Unable to create an instance of {targetType}.");
    }
}
