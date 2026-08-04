// Ported from WeKnora internal/infrastructure/chunker/splitter.go
// (https://github.com/Tencent/WeKnora). Core-only port: SplitText +
// SplitTextParentChild. All positions are UTF-16 char offsets; for the
// BMP text these match the rune offsets of the original Go port.
using System.Text.RegularExpressions;

namespace WeKnora.Chunker;

public static partial class WeKnoraChunker
{
    // Default chunk sizing constants mirror WeKnora.
    public const int DefaultChunkSize = 512;
    public const int DefaultChunkOverlap = 80;
    public const int AbsoluteMaxSize = 7500;

    // Regex patterns for content that must not be split.
    private static readonly Regex[] ProtectedPatterns =
    [
        LatexBlockMath(),   // LaTeX block math
        MarkdownImage(),    // Markdown images
        MarkdownLink(),     // Markdown links
        TableHeaderSeparator(), // Table header + separator
        TableRow(),         // Table rows
        FencedCode(),       // Fenced code blocks
    ];

    [GeneratedRegex(@"(?s)\$\$.*?\$\$")]
    private static partial Regex LatexBlockMath();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]+\)")]
    private static partial Regex MarkdownImage();

    [GeneratedRegex(@"\[[^\]]*\]\([^)]+\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(?m)[ ]*(?:\|[^|\n]*)+\|[\r\n]+\s*(?:\|\s*:?-{3,}:?\s*)+\|?[\r\n]+")]
    private static partial Regex TableHeaderSeparator();

    [GeneratedRegex(@"(?m)[ ]*(?:\|[^|\n]*)+\|[\r\n]+")]
    private static partial Regex TableRow();

    [GeneratedRegex(@"(?s)```(?:\w+)?[\r\n].*?```")]
    private static partial Regex FencedCode();

    /// <summary>Splits text into chunks with overlap, respecting protected patterns.</summary>
    public static List<Chunk> SplitText(string text, SplitterConfig cfg)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var chunkSize = cfg.ChunkSize;
        var chunkOverlap = cfg.ChunkOverlap;
        var separators = cfg.Separators;

        if (chunkSize <= 0)
            chunkSize = DefaultChunkSize;
        if (chunkOverlap < 0)
            chunkOverlap = 0;

        var protected_ = ProtectedSpans(text);
        var units = BuildUnitsWithProtection(text, protected_, separators, chunkSize);
        return MergeUnits(units, chunkSize, chunkOverlap);
    }

    /// <summary>
    /// Two-level chunking: 1) split text into large parent chunks, 2) split each
    /// parent into smaller child chunks. Child Seq is globally unique per document.
    /// </summary>
    public static ParentChildResult SplitTextParentChild(string text, SplitterConfig parentCfg, SplitterConfig childCfg)
    {
        if (string.IsNullOrEmpty(text))
            return new ParentChildResult();

        var parents = SplitText(text, parentCfg);
        if (parents.Count == 0)
            return new ParentChildResult();

        var newParents = new List<Chunk>();
        var children = new List<ChildChunk>();
        var childSeq = 0;

        foreach (var parent in parents)
        {
            var subs = SplitText(parent.Content, childCfg);

            var parentIndex = -1;
            if (subs.Count > 1 || (subs.Count == 1 && subs[0].Content != parent.Content))
            {
                parentIndex = newParents.Count;
                newParents.Add(parent);
            }

            foreach (var sub in subs)
            {
                sub.Seq = childSeq;
                sub.Start += parent.Start;
                sub.End += parent.Start;
                children.Add(new ChildChunk { Chunk = sub, ParentIndex = parentIndex });
                childSeq++;
            }
        }

        return new ParentChildResult { Parents = newParents, Children = children };
    }

    // ---- Protected spans ----

    /// <summary>Finds all non-overlapping protected regions in text.</summary>
    private static List<(int start, int end)> ProtectedSpans(string text)
    {
        var all = new List<(int start, int end)>();
        foreach (var pat in ProtectedPatterns)
        {
            foreach (Match m in pat.Matches(text))
            {
                if (m.Length > 0)
                    all.Add((m.Index, m.Index + m.Length));
            }
        }

        if (all.Count == 0)
            return [];

        // Sort by start, then by length descending.
        all.Sort((a, b) => a.start != b.start
            ? a.start.CompareTo(b.start)
            : (b.end - b.start).CompareTo(a.end - a.start));

        // Remove overlaps.
        var result = new List<(int start, int end)>();
        var lastEnd = 0;
        foreach (var m in all)
        {
            if (m.start >= lastEnd)
            {
                result.Add(m);
                lastEnd = m.end;
            }
        }
        return result;
    }

    // ---- Separator splitting ----

    private static List<string> SplitBySeparators(string text, string[] separators, int chunkSize)
    {
        if (string.IsNullOrEmpty(text) || separators.Length == 0)
            return [text];
        if (chunkSize > 0 && text.Length <= chunkSize)
            return [text];

        for (var i = 0; i < separators.Length; i++)
        {
            var sep = separators[i];
            if (sep.Length == 0)
                continue;

            var re = new Regex("(" + Regex.Escape(sep) + ")");
            var splits = re.Split(text);
            var matches = re.Matches(text).Select(m => m.Value).ToList();
            if (matches.Count == 0)
                continue;

            var pieces = new List<string>();
            for (var j = 0; j < splits.Length; j++)
            {
                if (splits[j].Length > 0)
                    pieces.Add(splits[j]);
                if (j < matches.Count && matches[j].Length > 0)
                    pieces.Add(matches[j]);
            }
            if (pieces.Count <= 1)
                continue;

            var out_ = new List<string>();
            var remaining = separators.Skip(i + 1).ToArray();
            foreach (var p in pieces)
            {
                if (chunkSize > 0 && p.Length > chunkSize && remaining.Length > 0)
                    out_.AddRange(SplitBySeparators(p, remaining, chunkSize));
                else
                    out_.Add(p);
            }
            return out_;
        }

        return [text];
    }

    // ---- Unit building ----

    /// <summary>
    /// Splits text into units, preserving protected spans as atomic. Start/End
    /// positions are char offsets. Protected spans larger than maxProtectedSize
    /// are forcibly split to avoid oversized chunks.
    /// </summary>
    private static List<SplitUnit> BuildUnitsWithProtection(string text, List<(int start, int end)> protected_, string[] separators, int chunkSize)
    {
        const int maxProtectedSize = 7500;

        var units = new List<SplitUnit>();
        var bytePos = 0;
        var runePos = 0;

        foreach (var p in protected_)
        {
            if (p.start > bytePos)
            {
                var pre = text[bytePos..p.start];
                var parts = SplitBySeparators(pre, separators, chunkSize);
                var runeOffset = runePos;
                foreach (var part in parts)
                {
                    var partLen = part.Length;
                    units.Add(new SplitUnit(part, runeOffset, runeOffset + partLen));
                    runeOffset += partLen;
                }
                runePos += pre.Length;
            }

            var protText = text[p.start..p.end];
            var protLen = protText.Length;

            if (protLen > maxProtectedSize)
            {
                var offset = 0;
                while (offset < protLen)
                {
                    var chunkEnd = offset + maxProtectedSize;
                    if (chunkEnd > protLen)
                    {
                        chunkEnd = protLen;
                    }
                    else
                    {
                        for (var i = chunkEnd - 1; i > offset && i > chunkEnd - 200; i--)
                        {
                            if (protText[i] == '\n' || protText[i] == ' ')
                            {
                                chunkEnd = i + 1;
                                break;
                            }
                        }
                    }

                    var chunkText = protText[offset..chunkEnd];
                    var chunkLen = chunkEnd - offset;
                    units.Add(new SplitUnit(chunkText, runePos + offset, runePos + offset + chunkLen));
                    offset = chunkEnd;
                }
            }
            else
            {
                units.Add(new SplitUnit(protText, runePos, runePos + protLen));
            }

            runePos += protLen;
            bytePos = p.end;
        }

        if (bytePos < text.Length)
        {
            var remaining = text[bytePos..];
            var parts = SplitBySeparators(remaining, separators, chunkSize);
            var runeOffset = runePos;
            foreach (var part in parts)
            {
                var partLen = part.Length;
                units.Add(new SplitUnit(part, runeOffset, runeOffset + partLen));
                runeOffset += partLen;
            }
        }

        return units;
    }

    // ---- Merging ----

    private static List<Chunk> MergeUnits(List<SplitUnit> units, int chunkSize, int chunkOverlap)
    {
        if (units.Count == 0)
            return [];

        var ht = new HeaderTracker();

        var chunks = new List<Chunk>();
        var current = new List<SplitUnit>();
        var curLen = 0;

        foreach (var u in units)
        {
            var uLen = u.Text.Length;

            // Oversized single unit: force split further.
            if (uLen > AbsoluteMaxSize)
            {
                if (current.Count > 0)
                {
                    chunks.Add(BuildChunk(current, chunks.Count));
                    current = [];
                    curLen = 0;
                }

                ht.Update(u.Text);

                var offset = 0;
                while (offset < uLen)
                {
                    var chunkEnd = offset + AbsoluteMaxSize;
                    if (chunkEnd > uLen)
                    {
                        chunkEnd = uLen;
                    }
                    else
                    {
                        for (var i = chunkEnd - 1; i > offset && i > chunkEnd - 200; i--)
                        {
                            if (u.Text[i] == '\n' || u.Text[i] == ' ')
                            {
                                chunkEnd = i + 1;
                                break;
                            }
                        }
                    }

                    var chunkText = u.Text[offset..chunkEnd];
                    chunks.Add(new Chunk { Content = chunkText, Seq = chunks.Count, Start = u.Start + offset, End = u.Start + chunkEnd });
                    offset = chunkEnd;
                }
                continue;
            }

            ht.Update(u.Text);
            if (ht.HeaderEndedThisUnit && current.Count > 0)
            {
                chunks.Add(BuildChunk(current, chunks.Count));
                current = [];
                curLen = 0;
            }

            var headers = ht.GetHeaders();
            var headersLen = headers.Length;
            if (headersLen > chunkSize)
            {
                headers = "";
                headersLen = 0;
            }

            if (curLen + uLen + headersLen > chunkSize && current.Count > 0)
            {
                chunks.Add(BuildChunk(current, chunks.Count));

                (current, curLen) = ComputeOverlap(current, chunkOverlap, chunkSize, uLen);

                if (headers.Length > 0 && headersLen + uLen <= chunkSize)
                {
                    while (current.Count > 0 && curLen + uLen + headersLen > chunkSize)
                    {
                        curLen -= current[0].Text.Length;
                        current = current.Skip(1).ToList();
                    }

                    var overlapText = UnitsText(current);
                    if (!HeaderAlreadyPresent(headers, overlapText, u.Text) &&
                        !HeaderColumnMismatch(headers, u.Text))
                    {
                        var startPos = u.Start;
                        if (current.Count > 0)
                            startPos = current[0].Start;
                        var hUnit = new SplitUnit(headers, startPos, startPos);
                        current.Insert(0, hUnit);
                        curLen += headersLen;
                    }
                }
            }

            if (curLen + uLen > AbsoluteMaxSize)
            {
                if (current.Count > 0)
                {
                    chunks.Add(BuildChunk(current, chunks.Count));
                    current = [];
                    curLen = 0;
                }
            }

            current.Add(u);
            curLen += uLen;
        }

        if (current.Count > 0)
            chunks.Add(BuildChunk(current, chunks.Count));

        return chunks;
    }

    private static string UnitsText(List<SplitUnit> units)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var u in units)
            sb.Append(u.Text);
        return sb.ToString();
    }

    private static Chunk BuildChunk(List<SplitUnit> units, int seq)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var u in units)
            sb.Append(u.Text);
        return new Chunk
        {
            Content = sb.ToString(),
            Seq = seq,
            Start = units[0].Start,
            End = units[^1].End,
        };
    }

    private static (List<SplitUnit>, int) ComputeOverlap(List<SplitUnit> current, int chunkOverlap, int chunkSize, int nextLen)
    {
        if (chunkOverlap <= 0)
            return ([], 0);

        var overlapLen = 0;
        var startIdx = current.Count;
        for (var i = current.Count - 1; i >= 0; i--)
        {
            var uLen = current[i].Text.Length;
            if (overlapLen + uLen > chunkOverlap)
                break;
            if (overlapLen + uLen + nextLen > chunkSize)
                break;
            overlapLen += uLen;
            startIdx = i;
        }

        // Skip leading separator-only and header-marker units in the overlap.
        while (startIdx < current.Count)
        {
            var u = current[startIdx];
            var isHeaderMarker = u.Start == u.End;
            var trimmed = u.Text.Trim();
            if (isHeaderMarker || trimmed.Length == 0 || IsSeparatorOnly(u.Text))
            {
                overlapLen -= u.Text.Length;
                startIdx++;
            }
            else
            {
                break;
            }
        }

        if (startIdx >= current.Count)
            return ([], 0);

        var overlap = current.Skip(startIdx).ToList();
        return (overlap, overlapLen);
    }

    private static bool IsSeparatorOnly(string s)
    {
        foreach (var r in s)
        {
            if (r != '\n' && r != '\r' && r != ' ' && r != '\t' && r != '。')
                return false;
        }
        return true;
    }

    private static readonly Regex TableRowRegex = new(@"(?m)^\s*(?:\|[^|\n]*)+" + "\\|\\s*$", RegexOptions.Compiled);

    /// <summary>True if the column-name row from the header is already present in the overlap or the next unit.</summary>
    private static bool HeaderAlreadyPresent(string headers, string overlapText, string unitText)
    {
        if (overlapText.Contains(headers) || unitText.Contains(headers))
            return true;

        var colRow = HeaderColumnRow(headers);
        if (colRow.Length == 0)
            return false;

        return overlapText.Contains(colRow) || unitText.Contains(colRow);
    }

    /// <summary>Extracts the column-name line from a header string.</summary>
    private static string HeaderColumnRow(string header)
    {
        foreach (var line in header.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.Contains("---"))
                continue;

            var onlyPipes = true;
            foreach (var r in trimmed)
            {
                if (r != '|' && r != ' ' && r != '\t')
                {
                    onlyPipes = false;
                    break;
                }
            }
            if (!onlyPipes)
                return trimmed;
        }
        return "";
    }

    /// <summary>True if the next split unit starts a table whose width differs from the active header.</summary>
    private static bool HeaderColumnMismatch(string headers, string nextUnit)
    {
        var headerCols = HeaderTableColumnCount(headers);
        var rowCols = FirstTableRowColumnCount(nextUnit);
        return headerCols > 0 && rowCols > 0 && headerCols != rowCols;
    }

    private static int FirstTableRowColumnCount(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || !TableRowRegex.IsMatch(trimmed))
                continue;
            return TableRowColumnCount(trimmed);
        }
        return 0;
    }

    private static int TableRowColumnCount(string line)
    {
        line = line.Trim();
        if (!line.StartsWith('|'))
            return 0;
        var parts = line.Split('|').ToList();
        if (parts.Count > 0 && string.IsNullOrWhiteSpace(parts[0]))
            parts.RemoveAt(0);
        if (parts.Count > 0 && string.IsNullOrWhiteSpace(parts[^1]))
            parts.RemoveAt(parts.Count - 1);
        return parts.Count;
    }

    private static int HeaderTableColumnCount(string header)
    {
        foreach (var line in header.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.Contains("---"))
                continue;
            if (TableRowColumnCount(trimmed) is var n && n > 0)
                return n;
        }
        return 0;
    }
}
