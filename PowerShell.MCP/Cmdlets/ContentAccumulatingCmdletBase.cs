using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Base class for cmdlets with Content pipeline accumulation functionality.
/// When Path/LiteralPath is specified as argument and Content is not,
/// accumulates pipeline input and processes in EndProcessing.
/// </summary>
public abstract class ContentAccumulatingCmdletBase : TextFileCmdletBase
{
    private List<object>? _contentBuffer;
    private bool _accumulateContent;

    /// <summary>
    /// Accessor to Content property defined in derived class
    /// </summary>
    protected abstract object[]? ContentProperty { get; set; }

    /// <summary>
    /// Whether in pipeline accumulation mode
    /// </summary>
    protected bool IsAccumulatingMode => _accumulateContent;

    /// <summary>
    /// Initializes pipeline accumulation mode.
    /// Call from BeginProcessing.
    /// </summary>
    protected void InitializeContentAccumulation()
    {
        bool pathFromArgument = MyInvocation.BoundParameters.ContainsKey("Path") ||
                                MyInvocation.BoundParameters.ContainsKey("LiteralPath");
        bool contentFromArgument = MyInvocation.BoundParameters.ContainsKey("Content");

        _accumulateContent = pathFromArgument && !contentFromArgument;
    }

    /// <summary>
    /// Accumulates pipeline input (when in accumulation mode).
    /// Call at start of ProcessRecord, return if true is returned.
    /// </summary>
    /// <returns>true if input was accumulated in accumulation mode</returns>
    protected bool TryAccumulateContent()
    {
        if (!_accumulateContent)
            return false;

        if (ContentProperty != null)
        {
            (_contentBuffer ??= []).AddRange(ContentProperty);
        }
        return true;
    }

    /// <summary>
    /// Sets accumulated Content to ContentProperty.
    /// Call in EndProcessing.
    /// </summary>
    /// <returns>true if there is accumulated content</returns>
    protected bool FinalizeAccumulatedContent()
    {
        if (!_accumulateContent)
            return false;

        if (_contentBuffer is { Count: > 0 })
        {
            ContentProperty = _contentBuffer.ToArray();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether accumulation buffer is empty
    /// </summary>
    protected bool IsContentBufferEmpty => _contentBuffer is null or { Count: 0 };
}