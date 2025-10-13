using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Encoding detection and management helper for text file operations
/// ULTRA-OPTIMIZED: Only reads first 64KB for detection
/// </summary>
internal static class EncodingHelper
{
    private const int DetectionBufferSize = 65536; // 64KB
    
    /// <summary>
    /// Detect encoding from file (BOM detection + heuristic)
    /// ULTRA-OPTIMIZED: Only reads first 64KB instead of entire file
    /// </summary>
    public static Encoding DetectEncoding(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        
        // Empty file defaults to UTF-8
        if (fileInfo.Length == 0)
            return new UTF8Encoding(false);
        
        // Read only first 64KB for detection
        int bufferSize = (int)Math.Min(DetectionBufferSize, fileInfo.Length);
        byte[] bytes = new byte[bufferSize];
        int bytesRead;
        
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
        {
            bytesRead = stream.Read(bytes, 0, bufferSize);
        }
        
        // BOM detection (only need first 4 bytes)
        if (bytesRead >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        if (bytesRead >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            // Check for UTF-32 LE
            if (bytesRead >= 4 && bytes[2] == 0x00 && bytes[3] == 0x00)
                return Encoding.UTF32;
            return Encoding.Unicode;
        }
        if (bytesRead >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        
        // Use Ude library for detection (it's already designed for partial content)
        try
        {
            var detector = new Ude.CharsetDetector();
            detector.Feed(bytes, 0, bytesRead);
            detector.DataEnd();
            
            if (detector.Charset != null)
            {
                try
                {
                    return Encoding.GetEncoding(detector.Charset);
                }
                catch
                {
                    // Encoding not found, continue to fallback
                }
            }
        }
        catch
        {
            // Ude library not available or failed, use fallback
        }
        
        // Fallback: Check if valid UTF-8
        // OPTIMIZATION: Use only the bytes we read (64KB max) instead of entire file
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            utf8.GetString(bytes, 0, bytesRead);  // Throws if not valid UTF-8
            return new UTF8Encoding(false);
        }
        catch
        {
            // Not valid UTF-8, assume system default
            return Encoding.Default;
        }
    }

    /// <summary>
    /// Get encoding from name or detect from file
    /// </summary>
    public static Encoding GetEncoding(string filePath, string? encodingName)
    {
        if (!string.IsNullOrEmpty(encodingName))
        {
            try
            {
                // 一般的なエンコーディングエイリアスの正規化
                return encodingName.ToLowerInvariant() switch
                {
                    // UTF encodings
                    "utf-8" or "utf8" => new UTF8Encoding(false),
                    "utf-8-bom" or "utf8-bom" or "utf8bom" => new UTF8Encoding(true),
                    "utf-16" or "utf16" or "utf-16le" or "utf16le" or "unicode" => Encoding.Unicode,
                    "utf-16be" or "utf16be" => Encoding.BigEndianUnicode,
                    "utf-32" or "utf32" or "utf-32le" or "utf32le" => Encoding.UTF32,
                    "utf-32be" or "utf32be" => new UTF32Encoding(true, true),
                    
                    // Japanese encodings
                    "shift_jis" or "shift-jis" or "shiftjis" or "sjis" => Encoding.GetEncoding("shift_jis"),
                    "euc-jp" or "euc_jp" or "eucjp" => Encoding.GetEncoding("euc-jp"),
                    "iso-2022-jp" or "iso2022jp" or "iso2022-jp" or "jis" => Encoding.GetEncoding("iso-2022-jp"),
                    
                    // Chinese encodings
                    "big-5" => Encoding.GetEncoding("big5"),
                    
                    // Korean encodings
                    "euckr" => Encoding.GetEncoding("euc-kr"),
                    
                    // Western encodings
                    "1252" => Encoding.GetEncoding("windows-1252"),
                    "latin-1" or "iso88591" => Encoding.GetEncoding("iso-8859-1"),
                    
                    // ASCII
                    "ascii" => Encoding.ASCII,
                    
                    _ => Encoding.GetEncoding(encodingName)
                };
            }
            catch
            {
                // Invalid encoding name specified, fall back to detection
            }
        }
        
        return DetectEncoding(filePath);
    }

    /// <summary>
    /// Try to upgrade encoding if needed (ASCII → UTF-8 for non-ASCII content)
    /// </summary>
    public static bool TryUpgradeEncodingIfNeeded(
        TextFileUtility.FileMetadata metadata, 
        string[] contentLines, 
        bool encodingExplicitlySpecified,
        out string? upgradeMessage)
    {
        upgradeMessage = null;
        
        // Don't upgrade if encoding explicitly specified
        if (encodingExplicitlySpecified)
            return false;
        
        // Only upgrade ASCII
        if (metadata.Encoding.CodePage != 20127) // US-ASCII
            return false;
        
        // Check for non-ASCII characters
        bool containsNonAscii = contentLines.Any(line => line.Any(c => c > 127));
        
        if (containsNonAscii)
        {
            metadata.Encoding = new UTF8Encoding(false);
            upgradeMessage = "Content contains non-ASCII characters. Upgrading encoding to UTF-8.";
            return true;
        }
        
        return false;
    }
}
