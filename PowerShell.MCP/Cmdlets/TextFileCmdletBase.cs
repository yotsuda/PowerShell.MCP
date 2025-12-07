using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Common base class for text file operation cmdlets
/// Provides common features like PS Drive path retention
/// </summary>
public abstract class TextFileCmdletBase : PSCmdlet
{
    /// <summary>
    /// Determines display path (retains PS Drive path, prefers shorter)
    /// </summary>
    protected string GetDisplayPath(string originalPath, string resolvedPath)
    {
        // Check if contains wildcard
        bool hasWildcard = originalPath.Contains('*') || originalPath.Contains('?');
        
        if (hasWildcard)
        {
            // For wildcards, keep directory part and replace filename
            return GetDisplayPathForWildcard(originalPath, resolvedPath);
        }
        
        // Check if PS Drive path
        if (IsPSDrivePath(originalPath))
        {
            // Return PS Drive path as-is
            return originalPath;
        }
        
        // For FileSystem absolute path, compare relative and absolute, use shorter
        var currentDirectory = SessionState.Path.CurrentFileSystemLocation.Path;
        var currentResolved = GetResolvedProviderPathFromPSPath(currentDirectory, out _).FirstOrDefault() ?? currentDirectory;
        
        var relativePath = TextFileUtility.GetRelativePath(currentResolved, resolvedPath);
        var absolutePath = resolvedPath;
        
        // Use relative path if shorter or equal length
        return relativePath.Length <= absolutePath.Length ? relativePath : absolutePath;
    }
    
    /// <summary>
    /// Generates display path when using wildcards
    /// </summary>
    protected string GetDisplayPathForWildcard(string originalPattern, string resolvedPath)
    {
        try
        {
            // Directory part of original pattern
            string? originalDir = System.IO.Path.GetDirectoryName(originalPattern);
            
            // Filename of resolved path
            string fileName = System.IO.Path.GetFileName(resolvedPath);
            
            if (string.IsNullOrEmpty(originalDir))
            {
                // No directory specified (e.g., *.txt)
                return fileName;
            }
            
            // Directory + filename
            return System.IO.Path.Combine(originalDir, fileName);
        }
        catch
        {
            // On error, calculate relative path from resolvedPath
            var currentDirectory = SessionState.Path.CurrentFileSystemLocation.Path;
            var currentResolved = GetResolvedProviderPathFromPSPath(currentDirectory, out _).FirstOrDefault() ?? currentDirectory;
            return TextFileUtility.GetRelativePath(currentResolved, resolvedPath);
        }
    }
    
    /// <summary>
    /// Determines if path is a PS Drive path
    /// </summary>
    protected static bool IsPSDrivePath(string path)
    {
        try
        {
            // Check if contains :
            if (!path.Contains(':'))
            {
                return false;
            }
            
            // FileSystem absolute paths (C:\, D:\, etc.) are not PS Drives
            if (System.IO.Path.IsPathRooted(path))
            {
                return false;
            }
            
            // Otherwise containing : = PS Drive (Temp:\, Env:\, etc.)
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates LineRange parameter
    /// Throws terminating error if 3 or more values specified
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
        // Note: 0 or negative end values are allowed (meaning end of file)
        if (lineRange != null && lineRange.Length == 2 && lineRange[1] > 0 && lineRange[0] > lineRange[1])
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException($"LineRange start ({lineRange[0]}) must be less than or equal to end ({lineRange[1]})"),
                "InvalidLineRange",
                ErrorCategory.InvalidArgument,
                lineRange));
        }
    }

    /// <summary>
    /// Exclusive check for -Contains and -Pattern
    /// </summary>
    protected static void ValidateContainsAndPatternMutuallyExclusive(string? contains, string? pattern)
    {
        if (!string.IsNullOrEmpty(contains) && !string.IsNullOrEmpty(pattern))
        {
            throw new PSArgumentException("Cannot specify both -Contains and -Pattern parameters.");
        }
    }

    /// <summary>
    /// Struct representing path resolution result
    /// </summary>
    protected struct ResolvedFileInfo
    {
        public string InputPath { get; set; }
        public string ResolvedPath { get; set; }
        public bool IsNewFile { get; set; }
    }

    /// <summary>
    /// Resolves file paths from -Path or -LiteralPath with existence check and error handling
    /// </summary>
    /// <param name="path">-Path parameter value (with wildcard expansion)</param>
    /// <param name="literalPath">-LiteralPath parameter value (without wildcard expansion)</param>
    /// <param name="allowNewFiles">Whether to allow new file creation</param>
    /// <param name="requireExisting">Whether to error if file does not exist</param>
    /// <returns>Iterator of resolved file information</returns>
    protected IEnumerable<ResolvedFileInfo> ResolveAndValidateFiles(
        string[]? path, 
        string[]? literalPath,
        bool allowNewFiles = false,
        bool requireExisting = true)
    {
        string[] inputPaths = path ?? literalPath ?? [];
        bool isLiteralPath = (literalPath != null);

        foreach (var inputPath in inputPaths)
        {
            System.Collections.ObjectModel.Collection<string>? resolvedPaths = null;
            ResolvedFileInfo? newFileInfo = null;
            bool isNewFile = false;
            bool hasError = false;
            
            try
            {
                if (isLiteralPath)
                {
                    // -LiteralPath: no wildcard expansion
                    var resolved = GetUnresolvedProviderPathFromPSPath(inputPath);
                    resolvedPaths = [resolved];
                }
                else
                {
                    // -Path: with wildcard expansion
                    resolvedPaths = GetResolvedProviderPathFromPSPath(inputPath, out _);
                }
            }
            catch (ItemNotFoundException)
            {
                if (allowNewFiles)
                {
                    // Attempt to create new file
                    try
                    {
                        var newPath = GetUnresolvedProviderPathFromPSPath(inputPath);
                        newFileInfo = new ResolvedFileInfo
                        {
                            InputPath = inputPath,
                            ResolvedPath = newPath,
                            IsNewFile = true
                        };
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
            
            // Execute yield return outside catch block
            if (newFileInfo != null)
            {
                yield return newFileInfo.Value;
                continue;
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
                
                yield return new ResolvedFileInfo
                {
                    InputPath = inputPath,
                    ResolvedPath = resolvedPath,
                    IsNewFile = isNewFile
                };
            }
        }
    }

    /// <summary>
    /// Checks if -WhatIf is explicitly specified
    /// </summary>
    protected bool IsWhatIfMode()
    {
        return MyInvocation.BoundParameters.ContainsKey("WhatIf") && 
               (SwitchParameter)MyInvocation.BoundParameters["WhatIf"];
    }
}
