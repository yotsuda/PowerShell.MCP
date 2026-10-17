using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using PowerShell.MCP.Proxy.Attributes;

namespace PowerShell.MCP.Proxy.Prompts;

[McpServerPromptType]
public class PowerShellPrompts
{
    [McpServerPrompt(Name = "foreign_language_dictation")]
    [LocalizedName("Prompt_ForeignLanguageDictationTraining_Name")]
    [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Description")]
    public static ChatMessage ForeignLanguageDictationTraining(
        [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Param_Request")]
        string? request = null)
    {
        var prompt = PromptTemplateLoader.Load("dictation.md", request);
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt(Name = "show_interactive_map")]
    [LocalizedName("Prompt_ShowInteractiveMap_Name")]
    [ResourceDescription("Prompt_ShowInteractiveMap_Description")]
    public static ChatMessage ShowInteractiveMap(
        [ResourceDescription("Prompt_ShowInteractiveMap_Param_Request")]
        string? request = null)
    {
        var prompt = PromptTemplateLoader.Load("map.md", request);
        return new ChatMessage(ChatRole.User, prompt);
    }
}
