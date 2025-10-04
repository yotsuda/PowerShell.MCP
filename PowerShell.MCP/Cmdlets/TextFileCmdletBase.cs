using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイル操作コマンドレットの共通基底クラス
/// PS Drive パス保持などの共通機能を提供
/// </summary>
public abstract class TextFileCmdletBase : PSCmdlet
{
    /// <summary>
    /// 表示用のパスを決定（PS Drive パスを保持、短い方を優先）
    /// </summary>
    protected string GetDisplayPath(string originalPath, string resolvedPath)
    {
        // ワイルドカードを含むか確認
        bool hasWildcard = originalPath.Contains('*') || originalPath.Contains('?');
        
        if (hasWildcard)
        {
            // ワイルドカードの場合、ディレクトリ部分を保持してファイル名を置き換え
            return GetDisplayPathForWildcard(originalPath, resolvedPath);
        }
        
        // PS Drive パスかチェック
        if (IsPSDrivePath(originalPath))
        {
            // PS Drive パスはそのまま返す
            return originalPath;
        }
        
        // FileSystem 絶対パスの場合、相対パスと絶対パスを比較して短い方を使用
        var currentDirectory = SessionState.Path.CurrentFileSystemLocation.Path;
        var currentResolved = GetResolvedProviderPathFromPSPath(currentDirectory, out _).FirstOrDefault() ?? currentDirectory;
        
        var relativePath = TextFileUtility.GetRelativePath(currentResolved, resolvedPath);
        var absolutePath = resolvedPath;
        
        // 相対パスの方が短い、または同じ長さなら相対パスを使用
        return relativePath.Length <= absolutePath.Length ? relativePath : absolutePath;
    }
    
    /// <summary>
    /// ワイルドカード使用時の表示パスを生成
    /// </summary>
    protected string GetDisplayPathForWildcard(string originalPattern, string resolvedPath)
    {
        try
        {
            // 元のパターンのディレクトリ部分
            string? originalDir = System.IO.Path.GetDirectoryName(originalPattern);
            
            // 解決されたパスのファイル名
            string fileName = System.IO.Path.GetFileName(resolvedPath);
            
            if (string.IsNullOrEmpty(originalDir))
            {
                // ディレクトリ指定なし（*.txt など）
                return fileName;
            }
            
            // ディレクトリ + ファイル名
            return System.IO.Path.Combine(originalDir, fileName);
        }
        catch
        {
            // エラー時は resolvedPath から相対パスを計算
            var currentDirectory = SessionState.Path.CurrentFileSystemLocation.Path;
            var currentResolved = GetResolvedProviderPathFromPSPath(currentDirectory, out _).FirstOrDefault() ?? currentDirectory;
            return TextFileUtility.GetRelativePath(currentResolved, resolvedPath);
        }
    }
    
    /// <summary>
    /// PS Drive パスかどうかを判定
    /// </summary>
    protected static bool IsPSDrivePath(string path)
    {
        try
        {
            // : を含むかチェック
            if (!path.Contains(':'))
            {
                return false;
            }
            
            // FileSystem の絶対パス（C:\, D:\ など）は PS Drive ではない
            if (System.IO.Path.IsPathRooted(path))
            {
                return false;
            }
            
            // それ以外で : を含む = PS Drive （Temp:\, Env:\, など）
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// LineRangeパラメータをバリデーション
    /// 3個以上の値が指定された場合は終了エラーをthrow
    /// </summary>
    protected void ValidateLineRange(int[]? lineRange)
    {
        if (lineRange != null && lineRange.Length > 2)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("LineRange accepts 1 or 2 values: start line, or start and end line. For example: -LineRange 5 or -LineRange 10,20"),
                "InvalidLineRange",
                ErrorCategory.InvalidArgument,
                lineRange));
        }
        
        // Validate start <= end when range is specified
        if (lineRange != null && lineRange.Length == 2 && lineRange[0] > lineRange[1])
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException($"LineRange start ({lineRange[0]}) must be less than or equal to end ({lineRange[1]})"),
                "InvalidLineRange",
                ErrorCategory.InvalidArgument,
                lineRange));
        }
    }

    /// <summary>
    /// -Path または -LiteralPath からファイルパスを解決
    /// </summary>
    /// <param name="path">-Path パラメータの値（ワイルドカード展開あり）</param>
    /// <param name="literalPath">-LiteralPath パラメータの値（ワイルドカード展開なし）</param>
    /// <returns>解決されたファイルパスのコレクション</returns>
    protected System.Collections.ObjectModel.Collection<string> ResolvePaths(string[]? path, string[]? literalPath)
    {
        if (path != null && path.Length > 0)
        {
            // -Path: ワイルドカード展開あり
            var allPaths = new System.Collections.ObjectModel.Collection<string>();
            foreach (var p in path)
            {
                try
                {
                    var resolved = GetResolvedProviderPathFromPSPath(p, out _);
                    foreach (var r in resolved)
                    {
                        allPaths.Add(r);
                    }
                }
                catch (ItemNotFoundException)
                {
                    // ファイルが存在しない場合は呼び出し元で処理
                    throw;
                }
            }
            return allPaths;
        }
        else if (literalPath != null && literalPath.Length > 0)
        {
            // -LiteralPath: ワイルドカード展開なし
            var allPaths = new System.Collections.ObjectModel.Collection<string>();
            foreach (var lp in literalPath)
            {
                try
                {
                    // GetUnresolvedProviderPathFromPSPath はワイルドカードを展開しない
                    var resolved = GetUnresolvedProviderPathFromPSPath(lp);
                    allPaths.Add(resolved);
                }
                catch (ItemNotFoundException)
                {
                    // ファイルが存在しない場合は呼び出し元で処理
                    throw;
                }
            }
            return allPaths;
        }
        
        return [];
    }
}
