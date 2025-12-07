using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Content パイプライン蓄積機能を持つコマンドレットの基底クラス
/// Path/LiteralPath が引数で指定され、Content が引数で指定されていない場合、
/// パイプライン入力を蓄積して EndProcessing で処理する
/// </summary>
public abstract class ContentAccumulatingCmdletBase : TextFileCmdletBase
{
    private List<object>? _contentBuffer;
    private bool _accumulateContent;

    /// <summary>
    /// 派生クラスで定義する Content プロパティへのアクセサ
    /// </summary>
    protected abstract object[]? ContentProperty { get; set; }

    /// <summary>
    /// パイプライン蓄積モードかどうか
    /// </summary>
    protected bool IsAccumulatingMode => _accumulateContent;

    /// <summary>
    /// パイプライン蓄積モードを初期化
    /// BeginProcessing から呼び出す
    /// </summary>
    protected void InitializeContentAccumulation()
    {
        bool pathFromArgument = MyInvocation.BoundParameters.ContainsKey("Path") ||
                                MyInvocation.BoundParameters.ContainsKey("LiteralPath");
        bool contentFromArgument = MyInvocation.BoundParameters.ContainsKey("Content");

        _accumulateContent = pathFromArgument && !contentFromArgument;
    }

    /// <summary>
    /// パイプライン入力を蓄積（蓄積モードの場合）
    /// ProcessRecord の先頭で呼び出し、true が返った場合は return する
    /// </summary>
    /// <returns>蓄積モードで入力を蓄積した場合は true</returns>
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
    /// 蓄積された Content を ContentProperty に設定
    /// EndProcessing で呼び出す
    /// </summary>
    /// <returns>蓄積された内容がある場合は true</returns>
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
    /// 蓄積バッファが空かどうか
    /// </summary>
    protected bool IsContentBufferEmpty => _contentBuffer is null or { Count: 0 };
}