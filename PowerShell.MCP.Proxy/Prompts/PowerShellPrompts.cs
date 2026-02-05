using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using PowerShell.MCP.Proxy.Attributes;

namespace PowerShell.MCP.Proxy.Prompts;

[McpServerPromptType]
public class PowerShellPrompts
{
    [McpServerPrompt]
    [LocalizedName("Prompt_AnalyzeContent_Name")]
    [ResourceDescription("Prompt_AnalyzeContent_Description")]
    public static ChatMessage AnalyzeContent(
        [ResourceDescription("Prompt_AnalyzeContent_Param_Request")]
        string request)
    {
        var prompt = PromptTemplateLoader.Load("analyze.md", request);
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_HtmlGenerationGuidelinesForAi_Name")]
    [ResourceDescription("Prompt_HtmlGenerationGuidelinesForAi_Description")]
    public static ChatMessage HtmlGenerationGuidelinesForAi()
    {
        var prompt = PromptTemplateLoader.Load("html-guidelines.md");
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_LearnProgrammingAndCli_Name")]
    [ResourceDescription("Prompt_LearnProgrammingAndCli_Description")]
    public static ChatMessage LearnProgrammingAndCli(
        [ResourceDescription("Prompt_LearnProgrammingAndCli_Param_Request")]
        string request)
    {
        var prompt = PromptTemplateLoader.Load("learn.md", request);
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_CreateWorkProcedure_Name")]
    [ResourceDescription("Prompt_CreateWorkProcedure_Description")]
    public static ChatMessage CreateWorkProcedure(
        [ResourceDescription("Prompt_CreateWorkProcedure_Param_Request")]
        string request)
    {
        var prompt = PromptTemplateLoader.Load("create-procedure.md", request);
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_ExecuteWorkProcedure_Name")]
    [ResourceDescription("Prompt_ExecuteWorkProcedure_Description")]
    public static ChatMessage ExecuteWorkProcedure(
        [ResourceDescription("Prompt_ExecuteWorkProcedure_Param_Request")]
        string request)
    {
        var prompt = PromptTemplateLoader.Load("exec-procedure.md", request);
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_ForeignLanguageDictationTraining_Name")]
    [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Description")]
    public static ChatMessage ForeignLanguageDictationTraining(
        [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Param_Request")]
        string request)
    {
        var prompt = PromptTemplateLoader.Load("dictation.md", request);
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_ShowInteractiveMap_Name")]
    [ResourceDescription("Prompt_ShowInteractiveMap_Description")]
    public static ChatMessage ShowInteractiveMap(
        [ResourceDescription("Prompt_ShowInteractiveMap_Param_Request")]
        string request)
    {
        var prompt = PromptTemplateLoader.Load("map.md", request);
        return new ChatMessage(ChatRole.User, prompt);
    }
}
