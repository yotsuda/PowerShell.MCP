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
    /// -Contains と -Pattern の排他チェック
    /// </summary>
    protected void ValidateContainsAndPatternMutuallyExclusive(string? contains, string? pattern)
    {
        if (!string.IsNullOrEmpty(contains) && !string.IsNullOrEmpty(pattern))
        {
            throw new PSArgumentException("Cannot specify both -Contains and -Pattern parameters.");
        }
    }

    /// <summary>
    /// パス解決結果を表す構造体
    /// </summary>
    protected struct ResolvedFileInfo
    {
        public string InputPath { get; set; }
        public string ResolvedPath { get; set; }
        public bool IsNewFile { get; set; }
    }

    /// <summary>
    /// -Path または -LiteralPath からファイルパスを解決し、存在チェックとエラーハンドリングを行う
    /// </summary>
    /// <param name="path">-Path パラメータの値（ワイルドカード展開あり）</param>
    /// <param name="literalPath">-LiteralPath パラメータの値（ワイルドカード展開なし）</param>
    /// <param name="allowNewFiles">新規ファイル作成を許可するか</param>
    /// <param name="requireExisting">ファイルが存在しない場合にエラーを出すか</param>
    /// <returns>解決されたファイル情報のイテレータ</returns>
    protected IEnumerable<ResolvedFileInfo> ResolveAndValidateFiles(
        string[]? path, 
        string[]? literalPath,
        bool allowNewFiles = false,
        bool requireExisting = true)
    {
        var results = new List<ResolvedFileInfo>();
        string[] inputPaths = path ?? literalPath ?? Array.Empty<string>();
        bool isLiteralPath = (literalPath != null);

        foreach (var inputPath in inputPaths)
        {
            System.Collections.ObjectModel.Collection<string>? resolvedPaths = null;
            bool isNewFile = false;
            bool hasError = false;
            
            try
            {
                if (isLiteralPath)
                {
                    // -LiteralPath: ワイルドカード展開なし
                    var resolved = GetUnresolvedProviderPathFromPSPath(inputPath);
                    resolvedPaths = new System.Collections.ObjectModel.Collection<string> { resolved };
                }
                else
                {
                    // -Path: ワイルドカード展開あり
                    resolvedPaths = GetResolvedProviderPathFromPSPath(inputPath, out _);
                }
            }
            catch (ItemNotFoundException)
            {
                if (allowNewFiles)
                {
                    // 新規ファイル作成を試みる
                    try
                    {
                        var newPath = GetUnresolvedProviderPathFromPSPath(inputPath);
                        results.Add(new ResolvedFileInfo
                        {
                            InputPath = inputPath,
                            ResolvedPath = newPath,
                            IsNewFile = true
                        });
                    }
                    catch (Exception ex)
                    {
                        WriteError(new ErrorRecord(
                            ex,
                            "PathResolutionFailed",
                            ErrorCategory.InvalidArgument,
                            inputPath));
                        hasError = true;
                    }
                }
                else
                {
                    WriteError(new ErrorRecord(
                        new FileNotFoundException($"File not found: {inputPath}"),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        inputPath));
                    hasError = true;
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(
                    ex,
                    "PathResolutionFailed",
                    ErrorCategory.InvalidArgument,
                    inputPath));
                hasError = true;
            }
            
            if (hasError || resolvedPaths == null)
            {
                continue;
            }
            
            foreach (var resolvedPath in resolvedPaths)
            {
                bool fileExists = File.Exists(resolvedPath);
                
                if (!fileExists)
                {
                    if (allowNewFiles)
                    {
                        isNewFile = true;
                    }
                    else if (requireExisting)
                    {
                        WriteError(new ErrorRecord(
                            new FileNotFoundException($"File not found: {inputPath}"),
                            "FileNotFound",
                            ErrorCategory.ObjectNotFound,
                            resolvedPath));
                        continue;
                    }
                }
                
                results.Add(new ResolvedFileInfo
                {
                    InputPath = inputPath,
                    ResolvedPath = resolvedPath,
                    IsNewFile = isNewFile
                });
            }
        }
        
        return results;
    }
}
