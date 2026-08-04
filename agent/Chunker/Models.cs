// Ported from WeKnora internal/infrastructure/chunker/splitter.go
// (https://github.com/Tencent/WeKnora). Core-only port: recursive text
// splitter (SplitText) + two-level parent/child chunking (SplitTextParentChild).
namespace WeKnora.Chunker;

/// <summary>Configures the text splitter.</summary>
public sealed class SplitterConfig
{
    public int ChunkSize { get; set; }
    public int ChunkOverlap { get; set; }
    public string[] Separators { get; set; } = ["\n\n", "\n", "。"];
}

/// <summary>A piece of split text with position tracking.</summary>
public sealed class Chunk
{
    public string Content { get; set; } = "";
    public string ContextHeader { get; set; } = "";
    public int Seq { get; set; }
    public int Start { get; set; }
    public int End { get; set; }

    /// <summary>Text fed to an embedding model — context header + content.</summary>
    public string EmbeddingContent()
    {
        var body = Content.Trim();
        if (ContextHeader.Length == 0)
            return body;
        return ContextHeader + "\n\n" + body;
    }
}

/// <summary>Extends <see cref="Chunk"/> with a reference to its parent.</summary>
public sealed class ChildChunk
{
    public Chunk Chunk { get; set; } = new();
    public int ParentIndex { get; set; } = -1;
}

/// <summary>Holds the two-level chunking output.</summary>
public sealed class ParentChildResult
{
    public List<Chunk> Parents { get; set; } = [];
    public List<ChildChunk> Children { get; set; } = [];
}

/// <summary>A unit of text with its original position (char offsets).</summary>
internal readonly record struct SplitUnit(string Text, int Start, int End);
