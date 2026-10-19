using System.Text;
using System.Text.RegularExpressions;

namespace MultiFind2;

public enum ContentResult { Match, NoMatch, Skipped }

/// <summary>
/// Decides whether a file's contents satisfy the criteria. Streams in bounded chunks with an
/// overlap window so arbitrarily large files never allocate more than the buffer, and a match
/// spanning a chunk boundary is still found.
/// </summary>
public sealed class ContentMatcher
{
    private const int ChunkChars = 1 << 20;   // 1M chars
    private const int ChunkBytes = 1 << 20;
    private const int BinarySniffBytes = 8192;

    private readonly SearchCriteria _c;
    private readonly string _needle = "";
    private readonly byte[] _needleBytes = Array.Empty<byte>();
    private readonly Regex? _regex;
    private readonly StringComparison _cmp;

    public ContentMatcher(SearchCriteria criteria)
    {
        _c = criteria;
        _cmp = criteria.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (!criteria.HasContentFilter) return;

        switch (criteria.ContentMode)
        {
            case ContentMode.Hex:
                _needleBytes = ParseHex(criteria.Contents);
                break;

            case ContentMode.Regex:
                var opts = RegexOptions.Compiled | RegexOptions.Multiline;
                if (!criteria.CaseSensitive) opts |= RegexOptions.IgnoreCase;
                var pattern = criteria.WholeWords ? $@"\b(?:{criteria.Contents})\b" : criteria.Contents;
                _regex = new Regex(pattern, opts, TimeSpan.FromSeconds(5));
                break;

            default:
                _needle = criteria.Contents;
                break;
        }
    }

    /// <summary>Throws FormatException on malformed input so the UI can report it before searching.</summary>
    public static byte[] ParseHex(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) continue;
            if (!Uri.IsHexDigit(ch)) throw new FormatException("Text must be in hex format");
            clean.Append(ch);
        }
        if (clean.Length == 0 || clean.Length % 2 != 0)
            throw new FormatException("Text must be in hex format");

        var bytes = new byte[clean.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(clean.ToString(i * 2, 2), 16);
        return bytes;
    }

    /// <summary>The longest needle length, used to size the overlap window.</summary>
    private int NeedleLength => _c.ContentMode switch
    {
        ContentMode.Hex => _needleBytes.Length,
        ContentMode.Regex => 0,
        _ => _needle.Length
    };

    public ContentResult Match(string path, out string? skipReason, CancellationToken token)
    {
        skipReason = null;
        if (!_c.HasContentFilter) return ContentResult.Match;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                      bufferSize: 64 * 1024, FileOptions.SequentialScan);
        return Match(fs, out skipReason, token);
    }

    /// <summary>
    /// The same decision against bytes that are already open. <paramref name="content"/> has to be
    /// seekable: the binary sniff reads the head and rewinds. The stream is left open -- whoever
    /// produced it owns it.
    /// </summary>
    public ContentResult Match(Stream content, out string? skipReason, CancellationToken token)
    {
        skipReason = null;
        if (!_c.HasContentFilter) return ContentResult.Match;

        if (content.Length > _c.MaxContentScanBytes)
        {
            skipReason = "File is too large to scan";
            return ContentResult.Skipped;
        }
        if (content.Length == 0) return ContentResult.NoMatch;

        if (_c.ContentMode == ContentMode.Hex)
            return ScanBytes(content, token) ? ContentResult.Match : ContentResult.NoMatch;

        if (_c.SkipBinary && LooksBinary(content))
        {
            skipReason = "File contains a NULL character";
            return ContentResult.Skipped;
        }

        return ScanText(content, token) ? ContentResult.Match : ContentResult.NoMatch;
    }

    private static bool LooksBinary(Stream fs)
    {
        var len = (int)Math.Min(BinarySniffBytes, fs.Length);
        var buf = new byte[len];
        var read = fs.Read(buf, 0, len);
        fs.Position = 0;

        // Skip a UTF-16 BOM: those legitimately contain NULs in ASCII ranges.
        if (read >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF)))
            return false;

        return Array.IndexOf(buf, (byte)0, 0, read) >= 0;
    }

    private bool ScanBytes(Stream fs, CancellationToken token)
    {
        var overlap = Math.Max(0, _needleBytes.Length - 1);
        var buf = new byte[ChunkBytes + overlap];
        var carried = 0;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            var read = fs.Read(buf, carried, ChunkBytes);
            if (read == 0) return false;

            var valid = carried + read;
            if (IndexOf(buf.AsSpan(0, valid), _needleBytes) >= 0) return true;

            carried = Math.Min(overlap, valid);
            if (carried > 0) Array.Copy(buf, valid - carried, buf, 0, carried);
        }
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
        => needle.IsEmpty ? -1 : haystack.IndexOf(needle);

    private bool ScanText(Stream fs, CancellationToken token)
    {
        // detectEncodingFromByteOrderMarks handles UTF-8/UTF-16LE/UTF-16BE BOMs;
        // UTF8 with throwOnInvalidBytes:false is a safe fallback for ANSI content.
        // leaveOpen: the stream belongs to the caller, who may not be done with it.
        using var reader = new StreamReader(fs, new UTF8Encoding(false),
                                            detectEncodingFromByteOrderMarks: true,
                                            bufferSize: 64 * 1024, leaveOpen: true);

        var overlap = Math.Max(0, NeedleLength - 1);
        if (_regex != null) overlap = 4096;   // regex needle length is unknown; use a generous window

        var buf = new char[ChunkChars + overlap];
        var carried = 0;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            var read = reader.Read(buf, carried, ChunkChars);
            if (read == 0) return false;

            var valid = carried + read;
            if (ChunkMatches(buf, valid)) return true;

            carried = Math.Min(overlap, valid);
            if (carried > 0) Array.Copy(buf, valid - carried, buf, 0, carried);
        }
    }

    private bool ChunkMatches(char[] buf, int length)
    {
        if (_regex != null)
        {
            try
            {
                return _regex.IsMatch(new string(buf, 0, length));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        var span = new ReadOnlySpan<char>(buf, 0, length);
        var from = 0;

        while (from <= span.Length - _needle.Length)
        {
            var hit = span[from..].IndexOf(_needle, _cmp);
            if (hit < 0) return false;

            var at = from + hit;
            if (!_c.WholeWords || IsWholeWord(span, at, _needle.Length)) return true;
            from = at + 1;
        }
        return false;
    }

    private static bool IsWholeWord(ReadOnlySpan<char> span, int start, int length)
    {
        if (start > 0 && IsWordChar(span[start - 1])) return false;
        var end = start + length;
        if (end < span.Length && IsWordChar(span[end])) return false;
        return true;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
