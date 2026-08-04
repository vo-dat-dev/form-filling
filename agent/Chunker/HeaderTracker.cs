// Ported from WeKnora internal/infrastructure/chunker/header_tracker.go
// (which itself ports docreader/splitter/header_hook.py). Tracks Markdown
// table headers so chunks split across a table keep their header context.
using System.Text.RegularExpressions;

namespace WeKnora.Chunker;

/// <summary>A pattern pair for detecting contextual headers.</summary>
internal sealed class HeaderTrackerHook
{
    public required Regex StartPattern { get; init; }
    public required Regex EndPattern { get; init; }
    public required int Priority { get; init; }
}

internal sealed class HeaderTracker
{
    private const int MarkdownTableHookPriority = 15;

    private static readonly HeaderTrackerHook[] DefaultHeaderHooks =
    [
        new()
        {
            // Markdown table: header row + separator row (e.g. "| A | B |\n| --- | --- |\n")
            StartPattern = new Regex(@"(?si)^\s*(?:\|[^|\n]*)+\|?[\r\n]+\s*(?:\|\s*:?-{3,}:?\s*)+\|?[\r\n]+$", RegexOptions.Compiled),
            // Empty/whitespace line or a line that doesn't start with | or whitespace
            EndPattern = new Regex(@"(?si)^\s*$|^\s*[^|\s].*$", RegexOptions.Compiled),
            Priority = MarkdownTableHookPriority,
        },
    ];

    private static readonly Regex TableRowPattern = new(@"(?m)^\s*(?:\|[^|\n]*)+\|\s*$", RegexOptions.Compiled);

    private readonly HeaderTrackerHook[] _hooks = DefaultHeaderHooks;
    private readonly Dictionary<int, string> _activeHeaders = [];
    private readonly HashSet<int> _endedHeaders = [];
    private readonly HashSet<int> _pendingExtend = [];
    private bool _pendingTableBreak;
    private bool _headerEndedThisUnit;

    public bool HeaderEndedThisUnit => _headerEndedThisUnit;

    public void Update(string split)
    {
        _headerEndedThisUnit = false;

        if (_pendingTableBreak)
        {
            _pendingTableBreak = false;
            if (_activeHeaders.ContainsKey(MarkdownTableHookPriority))
            {
                if (FirstTableRowColumnCount(split) > 0)
                {
                    ClearTableHeader();
                    _headerEndedThisUnit = true;
                }
                else
                {
                    ClearTableHeader();
                }
            }
        }

        // 1. Check for header-end markers among currently active headers.
        foreach (var hook in _hooks)
        {
            if (_activeHeaders.ContainsKey(hook.Priority) && hook.EndPattern.IsMatch(split))
            {
                _endedHeaders.Add(hook.Priority);
                _activeHeaders.Remove(hook.Priority);
                _pendingExtend.Remove(hook.Priority);
            }
        }

        // 1b. Paragraph splits consume the blank line between tables. Mark a
        // break after "| last row |\n\n" and resolve on the next unit; also end
        // when a new table row has a different column count than the active header.
        if (_activeHeaders.ContainsKey(MarkdownTableHookPriority))
        {
            if (!_pendingExtend.Contains(MarkdownTableHookPriority))
            {
                if (SplitEndsWithParagraphBreak(split))
                    _pendingTableBreak = true;
                else
                    EndTableHeaderOnColumnMismatch(split);
            }
        }

        // 2. Replace empty column-name headers (e.g. "||") with the first data row.
        foreach (var p in _pendingExtend.ToList())
        {
            if (_activeHeaders.TryGetValue(p, out var header) && TableRowPattern.IsMatch(split))
            {
                var sep = ExtractSeparatorLine(header);
                _activeHeaders[p] = split + sep;
            }
            _pendingExtend.Remove(p);
        }

        // 3. Check for new header-start markers.
        foreach (var hook in _hooks)
        {
            if (_activeHeaders.ContainsKey(hook.Priority) || _endedHeaders.Contains(hook.Priority))
                continue;

            var m = hook.StartPattern.Match(split);
            if (m.Success)
            {
                _activeHeaders[hook.Priority] = m.Value;
                if (IsEmptyTableHeaderRow(m.Value))
                    _pendingExtend.Add(hook.Priority);
            }
        }

        // 4. If all headers ended, clear the ended set so future tables can be tracked.
        if (_activeHeaders.Count == 0)
            _endedHeaders.Clear();
    }

    public string GetHeaders()
    {
        if (_activeHeaders.Count == 0)
            return "";

        var entries = _activeHeaders.OrderByDescending(kv => kv.Key).Select(kv => kv.Value).ToList();
        return string.Join("\n", entries);
    }

    private static bool IsEmptyTableHeaderRow(string header)
    {
        var idx = header.IndexOf('\n');
        if (idx < 0)
            return false;
        var row = header[..idx].Trim();
        foreach (var r in row)
        {
            if (r != '|' && r != ' ' && r != '\t')
                return false;
        }
        return true;
    }

    private static string ExtractSeparatorLine(string header)
    {
        foreach (var line in header.Split('\n'))
        {
            if (line.Contains("---"))
                return line + "\n";
        }
        return "";
    }

    private void ClearTableHeader()
    {
        _endedHeaders.Add(MarkdownTableHookPriority);
        _activeHeaders.Remove(MarkdownTableHookPriority);
        _pendingExtend.Remove(MarkdownTableHookPriority);
    }

    private void EndTableHeaderOnColumnMismatch(string split)
    {
        if (!_activeHeaders.TryGetValue(MarkdownTableHookPriority, out var header))
            return;
        var rowCols = FirstTableRowColumnCount(split);
        var headerCols = HeaderTableColumnCount(header);
        if (rowCols > 0 && headerCols > 0 && rowCols != headerCols)
        {
            ClearTableHeader();
            _headerEndedThisUnit = true;
        }
    }

    private static bool SplitEndsWithParagraphBreak(string split)
    {
        var trimmed = split.TrimEnd(' ', '\t', '\r');
        return trimmed.EndsWith("\n\n") || trimmed.EndsWith("\r\n\r\n");
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

    private static int FirstTableRowColumnCount(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && TableRowPattern.IsMatch(trimmed))
                return TableRowColumnCount(trimmed);
        }
        return 0;
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
