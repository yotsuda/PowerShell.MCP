using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// File metadata detection helper for text file operations
/// </summary>
internal static class FileMetadataHelper
{
    /// <summary>
    /// Detect newline sequence and trailing newline from file
    /// </summary>
    public static (string NewlineSequence, bool HasTrailingNewline) DetectNewline(
        string filePath, 
        Encoding encoding)
    {
        var fileInfo = new FileInfo(filePath);
        
        // Empty file defaults
        if (fileInfo.Length == 0)
            return (Environment.NewLine, false);

        // Read file content
        string content = File.ReadAllText(filePath, encoding);
        
        // Empty content defaults
        if (string.IsNullOrEmpty(content))
            return (Environment.NewLine, false);

        // Detect newline sequence
        string newlineSequence = Environment.NewLine; // Default

        if (content.Contains("\r\n"))
            newlineSequence = "\r\n";
        else if (content.Contains("\n"))
            newlineSequence = "\n";
        else if (content.Contains("\r"))
            newlineSequence = "\r";

        // Check trailing newline
        bool hasTrailingNewline = content.EndsWith("\r\n") || 
                                  content.EndsWith("\n") || 
                                  content.EndsWith("\r");

        return (newlineSequence, hasTrailingNewline);
    }

    /// <summary>
    /// Detect file metadata (encoding, newline, trailing newline)
    /// </summary>
    public static TextFileUtility.FileMetadata DetectFileMetadata(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        
        // Empty file defaults
        if (fileInfo.Length == 0)
        {
            return new TextFileUtility.FileMetadata
            {
                Encoding = new UTF8Encoding(false),
                NewlineSequence = Environment.NewLine,
                HasTrailingNewline = false
            };
        }
        
        var encoding = EncodingHelper.DetectEncoding(filePath);
        var (newline, hasTrailing) = DetectNewline(filePath, encoding);

        return new TextFileUtility.FileMetadata
        {
            Encoding = encoding,
            NewlineSequence = newline,
            HasTrailingNewline = hasTrailing
        };
    }

    /// <summary>
    /// Detect file metadata with optional explicit encoding
    /// </summary>
    public static TextFileUtility.FileMetadata DetectFileMetadata(
        string filePath, 
        string? encodingName)
    {
        var fileInfo = new FileInfo(filePath);
        
        // Empty file defaults
        if (fileInfo.Length == 0)
        {
            var defaultEncoding = string.IsNullOrEmpty(encodingName) 
                ? new UTF8Encoding(false) 
                : EncodingHelper.GetEncoding(filePath, encodingName);
            
            return new TextFileUtility.FileMetadata
            {
                Encoding = defaultEncoding,
                NewlineSequence = Environment.NewLine,
                HasTrailingNewline = false
            };
        }
        
        var encoding = EncodingHelper.GetEncoding(filePath, encodingName);
        var (newline, hasTrailing) = DetectNewline(filePath, encoding);

        return new TextFileUtility.FileMetadata
        {
            Encoding = encoding,
            NewlineSequence = newline,
            HasTrailingNewline = hasTrailing
        };
    }
}
