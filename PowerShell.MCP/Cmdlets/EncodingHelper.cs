using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Encoding detection and management helper for text file operations
/// </summary>
internal static class EncodingHelper
{
    /// <summary>
    /// Detect encoding from file (BOM detection + heuristic)
    /// </summary>
    public static Encoding DetectEncoding(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        
        // Empty file defaults to UTF-8
        if (bytes.Length == 0)
            return new UTF8Encoding(false);
        
        // BOM detection
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return Encoding.UTF32;
        
        // Use Ude library for detection if available
        try
        {
            var detector = new Ude.CharsetDetector();
            detector.Feed(bytes, 0, Math.Min(bytes.Length, 65536));
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
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            using var reader = new StreamReader(filePath, utf8, false);
            reader.ReadToEnd();
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
