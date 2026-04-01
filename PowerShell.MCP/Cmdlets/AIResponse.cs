namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Wraps an AI response text. Has a custom format (empty display) so that
/// WriteObject does not duplicate the streaming output shown via Host.UI.Write.
/// Behaves like a string in most contexts via implicit conversion and ToString().
/// </summary>
public class AIResponse
{
    public string Text { get; }
    public string Model { get; }
    public string Provider { get; }

    public AIResponse(string text, string model, string provider)
    {
        Text = text;
        Model = model;
        Provider = provider;
    }

    public override string ToString() => Text;

    public static implicit operator string(AIResponse r) => r.Text;

    public int Length => Text.Length;
}
