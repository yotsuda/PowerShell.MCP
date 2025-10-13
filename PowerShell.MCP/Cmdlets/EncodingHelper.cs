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
                return Encoding.GetEncoding(encodingName);
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
