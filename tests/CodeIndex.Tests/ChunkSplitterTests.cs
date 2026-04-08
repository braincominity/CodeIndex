using CodeIndex.Indexer;

namespace CodeIndex.Tests;

/// <summary>
/// Tests for ChunkSplitter.
/// ChunkSplitterのテスト。
/// </summary>
public class ChunkSplitterTests
{
    [Fact]
    public void Split_SmallFile_ReturnsSingleChunk()
    {
        // A file with fewer lines than chunk size should yield one chunk
        // チャンクサイズ未満の行数のファイルは1チャンクになる
        var content = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}"));
        var chunks = ChunkSplitter.Split(1, content);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(10, chunks[0].EndLine);
    }

    [Fact]
    public void Split_ExactChunkSize_ReturnsSingleChunk()
    {
        // Exactly 80 lines should yield one chunk
        // ちょうど80行なら1チャンクになる
        var content = string.Join('\n', Enumerable.Range(1, 80).Select(i => $"line {i}"));
        var chunks = ChunkSplitter.Split(1, content);

        Assert.Single(chunks);
        Assert.Equal(80, chunks[0].EndLine);
    }

    [Fact]
    public void Split_LargeFile_CreatesOverlappingChunks()
    {
        // 160 lines: chunk 0 = 1-80, chunk 1 = 71-150, chunk 2 = 141-160
        // 160行: チャンク0 = 1-80, チャンク1 = 71-150, チャンク2 = 141-160
        var content = string.Join('\n', Enumerable.Range(1, 160).Select(i => $"line {i}"));
        var chunks = ChunkSplitter.Split(1, content);

        Assert.True(chunks.Count >= 3);

        // Second chunk should start at line 71 (overlap of 10)
        // 2番目のチャンクは71行目から始まる（10行の重複）
        Assert.Equal(71, chunks[1].StartLine);
    }

    [Fact]
    public void Split_EmptyFile_ReturnsNoChunks()
    {
        // Empty content produces no chunks
        // 空のコンテンツではチャンクは生成されない
        var chunks = ChunkSplitter.Split(1, "");
        Assert.Empty(chunks);
    }

    [Fact]
    public void Split_ChunkIndex_IsSequential()
    {
        // Chunk indices should be sequential starting from 0
        // チャンクインデックスは0から連番になる
        var content = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"line {i}"));
        var chunks = ChunkSplitter.Split(1, content);

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public void Split_CrlfInput_NormalizesToLf()
    {
        // CRLF line endings should be normalized to LF before splitting
        // CRLF改行はLFに正規化されてから分割される
        var content = "line 1\r\nline 2\r\nline 3\r\n";
        var chunks = ChunkSplitter.Split(1, content);

        Assert.Single(chunks);
        Assert.Equal(3, chunks[0].EndLine);
        Assert.DoesNotContain("\r", chunks[0].Content);
    }
}
