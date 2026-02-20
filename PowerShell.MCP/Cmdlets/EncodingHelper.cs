using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Encoding detection and management helper for text file operations
/// ULTRA-OPTIMIZED: Only reads first 64KB for detection
/// </summary>
public static class EncodingHelper
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

        return FileMetadataHelper.DetectEncodingFromBytes(bytes, bytesRead);
    }


    /// <summary>
    /// Fast encoding detection for read-only operations (BOM check only, defaults to UTF-8)
    /// Use this for Show-TextFiles where exact encoding detection is not critical
    /// </summary>
    public static Encoding GetEncodingForReading(string filePath, string? encodingName)
    {
        // If encoding explicitly specified, use it
        if (!string.IsNullOrEmpty(encodingName))
        {
            return GetEncodingByName(encodingName) ?? new UTF8Encoding(false);
        }

        // BOM check only - fast path
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4, FileOptions.SequentialScan);
            Span<byte> bom = stackalloc byte[4];
            int bytesRead = stream.Read(bom);

            if (bytesRead >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return new UTF8Encoding(true);
            if (bytesRead >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            {
                if (bytesRead >= 4 && bom[2] == 0x00 && bom[3] == 0x00)
                    return Encoding.UTF32;
                return Encoding.Unicode;
            }
            if (bytesRead >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
                return Encoding.BigEndianUnicode;
        }
        catch
        {
            // Fall through to default
        }

        return new UTF8Encoding(false);
    }

    /// <summary>
    /// Canonical encoding alias resolver. Returns null if the name is unrecognized.
    /// All encoding name → Encoding mappings live here (single source of truth).
    /// </summary>
    private static Encoding? GetEncodingByName(string encodingName)
    {
        try
        {
            return encodingName.ToLowerInvariant() switch
            {
                // UTF encodings
                "utf-8" or "utf8" or "utf8nobom" or "utf-8nobom" or "utf-8-nobom" or "utf8-nobom" => new UTF8Encoding(false),
                "utf-8-bom" or "utf8-bom" or "utf8bom" or "utf8-sig" or "utf-8-sig" => new UTF8Encoding(true),
                "utf-16" or "utf16" or "utf-16le" or "utf16le" or "unicode" or "utf16bom" or "utf-16bom" or "utf16lebom" or "utf-16lebom" => Encoding.Unicode,
                "utf-16be" or "utf16be" or "utf16bebom" or "utf-16bebom" => Encoding.BigEndianUnicode,
                "utf-32" or "utf32" or "utf-32le" or "utf32le" or "utf32bom" or "utf-32bom" or "utf32lebom" or "utf-32lebom" => Encoding.UTF32,
                "utf-32be" or "utf32be" or "utf32bebom" or "utf-32bebom" => new UTF32Encoding(true, true),

                // Japanese encodings
                "shift_jis" or "shift-jis" or "shiftjis" or "sjis" or "cp932" => Encoding.GetEncoding("shift_jis"),
                "euc-jp" or "euc_jp" or "eucjp" => Encoding.GetEncoding("euc-jp"),
                "iso-2022-jp" or "iso2022jp" or "iso2022-jp" or "jis" => Encoding.GetEncoding("iso-2022-jp"),

                // Chinese encodings
                "big-5" or "big5hkscs" or "cp950" => Encoding.GetEncoding("big5"),
                "gb2312" or "gbk" or "gb18030" or "cp936" => Encoding.GetEncoding("gb2312"),

                // Korean encodings
                "euckr" or "cp949" => Encoding.GetEncoding("euc-kr"),

                // Windows codepages (numeric)
                "874" => Encoding.GetEncoding("windows-874"),     // Thai
                "1250" => Encoding.GetEncoding("windows-1250"),   // Central European
                "1251" => Encoding.GetEncoding("windows-1251"),   // Cyrillic
                "1252" => Encoding.GetEncoding("windows-1252"),   // Western European
                "1253" => Encoding.GetEncoding("windows-1253"),   // Greek
                "1254" => Encoding.GetEncoding("windows-1254"),   // Turkish
                "1255" => Encoding.GetEncoding("windows-1255"),   // Hebrew
                "1256" => Encoding.GetEncoding("windows-1256"),   // Arabic
                "1257" => Encoding.GetEncoding("windows-1257"),   // Baltic
                "1258" => Encoding.GetEncoding("windows-1258"),   // Vietnamese

                // Windows codepages (cp prefix)
                "cp874" => Encoding.GetEncoding("windows-874"),
                "cp1250" => Encoding.GetEncoding("windows-1250"),
                "cp1251" => Encoding.GetEncoding("windows-1251"),
                "cp1254" => Encoding.GetEncoding("windows-1254"),

                // ISO-8859 variants
                "latin-1" or "iso88591" or "iso_8859_1" => Encoding.GetEncoding("iso-8859-1"),   // Latin-1 (Western)
                "latin-2" or "iso88592" or "iso_8859_2" => Encoding.GetEncoding("iso-8859-2"),   // Latin-2 (Central European)
                "iso88595" or "iso_8859_5" => Encoding.GetEncoding("iso-8859-5"),                // Cyrillic
                "iso88596" => Encoding.GetEncoding("iso-8859-6"),                                // Arabic
                "iso88599" => Encoding.GetEncoding("iso-8859-9"),                                // Turkish
                "latin-9" or "iso885915" => Encoding.GetEncoding("iso-8859-15"),                 // Latin-9

                // Cyrillic encodings
                "koi8u" => Encoding.GetEncoding("koi8-u"),       // Ukrainian

                // Thai encodings
                "tis620" => Encoding.GetEncoding("tis-620"),

                // ASCII
                "ascii" => Encoding.ASCII,

                _ => Encoding.GetEncoding(encodingName)
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get encoding from name or detect from file.
    /// Delegates to GetEncodingByName for alias resolution.
    /// </summary>
    public static Encoding GetEncoding(string filePath, string? encodingName)
    {
        if (!string.IsNullOrEmpty(encodingName))
        {
            var resolved = GetEncodingByName(encodingName);
            if (resolved != null)
                return resolved;
            // Invalid encoding name, fall back to detection
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
