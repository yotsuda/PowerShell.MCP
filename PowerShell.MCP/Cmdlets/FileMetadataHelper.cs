using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// File metadata detection helper for text file operations
/// ULTRA-OPTIMIZED: Partial file reading - only reads necessary bytes (header + tail)
/// </summary>
internal static class FileMetadataHelper
{
    private const int HeaderBufferSize = 65536; // 64KB for encoding detection
    private const int TailBufferSize = 4096;    // 4KB for newline detection
    
    /// <summary>
    /// Read file header and tail efficiently
    /// </summary>
    private static (byte[] HeaderBytes, int HeaderLength, byte[] TailBytes, int TailLength) ReadFilePartially(string filePath, long fileLength)
    {
        byte[] headerBytes = new byte[Math.Min(HeaderBufferSize, fileLength)];
        byte[] tailBytes = Array.Empty<byte>();
        int headerLength = 0;
        int tailLength = 0;
        
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
        {
            // Read header
            headerLength = stream.Read(headerBytes, 0, headerBytes.Length);
            
            // Read tail if file is large enough
            if (fileLength > HeaderBufferSize)
            {
                tailBytes = new byte[Math.Min(TailBufferSize, fileLength - HeaderBufferSize)];
                stream.Seek(-tailBytes.Length, SeekOrigin.End);
                tailLength = stream.Read(tailBytes, 0, tailBytes.Length);
            }
            else
            {
                // For small files, tail is already in header
                tailBytes = headerBytes;
                tailLength = headerLength;
            }
        }
        
        return (headerBytes, headerLength, tailBytes, tailLength);
    }
    
    /// <summary>
    /// Detect newline sequence and trailing newline from bytes
    /// </summary>
    private static (string NewlineSequence, bool HasTrailingNewline) DetectNewlineFromBytes(
        byte[] headerBytes,
        int headerLength,
        byte[] tailBytes, 
        int tailLength,
        Encoding encoding,
        long fileLength)
    {
        if (fileLength == 0)
            return (Environment.NewLine, false);

        // Detect newline sequence from header
        string newlineSequence = Environment.NewLine;
        
        // Decode header to find newline pattern
        string headerContent = encoding.GetString(headerBytes, 0, headerLength);
        
        if (headerContent.Contains("\r\n"))
            newlineSequence = "\r\n";
        else if (headerContent.Contains("\n"))
            newlineSequence = "\n";
        else if (headerContent.Contains("\r"))
            newlineSequence = "\r";

        // Check trailing newline from tail bytes
        bool hasTrailingNewline = false;
        if (tailLength > 0)
        {
            // Check for \r\n (0x0D 0x0A)
            if (tailLength >= 2 && tailBytes[tailLength - 2] == 0x0D && tailBytes[tailLength - 1] == 0x0A)
                hasTrailingNewline = true;
            // Check for \n (0x0A) or \r (0x0D)
            else if (tailBytes[tailLength - 1] == 0x0A || tailBytes[tailLength - 1] == 0x0D)
                hasTrailingNewline = true;
        }

        return (newlineSequence, hasTrailingNewline);
    }

    /// <summary>
    /// Detect encoding from bytes (without reading entire file)
    /// </summary>
    private static Encoding DetectEncodingFromBytes(byte[] bytes, int length)
    {
        if (length == 0)
            return new UTF8Encoding(false);
        
        // BOM detection (only need first 4 bytes)
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            // Check for UTF-32 LE
            if (length >= 4 && bytes[2] == 0x00 && bytes[3] == 0x00)
                return Encoding.UTF32;
            return Encoding.Unicode;
        }
        if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;
        
        // Use Ude library for detection (already optimized to use only header)
        try
        {
            var detector = new Ude.CharsetDetector();
            detector.Feed(bytes, 0, length);
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
        
        // Fallback: Check if valid UTF-8 (using header bytes only)
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            utf8.GetString(bytes, 0, length);  // Throws if not valid UTF-8
            return new UTF8Encoding(false);
        }
        catch
        {
            // Not valid UTF-8, assume system default
            return Encoding.Default;
        }
    }

    /// <summary>
    /// Detect newline sequence and trailing newline from file
    /// OPTIMIZED: Only reads necessary parts
    /// </summary>
    public static (string NewlineSequence, bool HasTrailingNewline) DetectNewline(
        string filePath, 
        Encoding encoding)
    {
        var fileInfo = new FileInfo(filePath);
        
        // Empty file defaults
        if (fileInfo.Length == 0)
            return (Environment.NewLine, false);

        // Read only header and tail
        var (headerBytes, headerLength, tailBytes, tailLength) = ReadFilePartially(filePath, fileInfo.Length);
        return DetectNewlineFromBytes(headerBytes, headerLength, tailBytes, tailLength, encoding, fileInfo.Length);
    }

    /// <summary>
    /// Detect file metadata (encoding, newline, trailing newline)
    /// ULTRA-OPTIMIZED: Only reads header (64KB) + tail (4KB) instead of entire file
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
        
        // ULTRA-OPTIMIZATION: Read only header + tail (64KB + 4KB = 68KB max)
        // Even for 1GB file, only reads 68KB!
        var (headerBytes, headerLength, tailBytes, tailLength) = ReadFilePartially(filePath, fileInfo.Length);
        
        var encoding = DetectEncodingFromBytes(headerBytes, headerLength);
        var (newline, hasTrailing) = DetectNewlineFromBytes(headerBytes, headerLength, tailBytes, tailLength, encoding, fileInfo.Length);

        return new TextFileUtility.FileMetadata
        {
            Encoding = encoding,
            NewlineSequence = newline,
            HasTrailingNewline = hasTrailing
        };
    }

    /// <summary>
    /// Detect file metadata with optional explicit encoding
    /// ULTRA-OPTIMIZED: Partial file reading when auto-detecting
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
        
        // If encoding is explicitly specified, use it directly
        if (!string.IsNullOrEmpty(encodingName))
        {
            try
            {
                var encoding = EncodingHelper.GetEncoding(filePath, encodingName);
                
                // Still need to detect newline, but can be more efficient
                var (headerBytes, headerLength, tailBytes, tailLength) = ReadFilePartially(filePath, fileInfo.Length);
                var (newline, hasTrailing) = DetectNewlineFromBytes(headerBytes, headerLength, tailBytes, tailLength, encoding, fileInfo.Length);
                
                return new TextFileUtility.FileMetadata
                {
                    Encoding = encoding,
                    NewlineSequence = newline,
                    HasTrailingNewline = hasTrailing
                };
            }
            catch
            {
                // Invalid encoding name, fall through to auto-detection
            }
        }
        
        // Auto-detect both encoding and newline
        return DetectFileMetadata(filePath);
    }
}
