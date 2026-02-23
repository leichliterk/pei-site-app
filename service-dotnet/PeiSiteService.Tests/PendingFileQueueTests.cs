using PeiSiteService.Models;
using PeiSiteService.Services;
using Xunit;

namespace PeiSiteService.Tests;

public class PendingFileQueueTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PendingFileQueue _queue;
    private readonly FileLogger _logger = new();

    public PendingFileQueueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pei-queue-test-{Guid.NewGuid():N}");
        _queue = new PendingFileQueue(_tempDir, _logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static PendingFileEntry MakeEntry(string id, string serverId = "srv1") => new()
    {
        PendingFileId = id,
        ServerId = serverId,
        Filename = $"{id}.txt",
        ContentBase64 = "dGVzdA==",  // "test" in base64
        Sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
        Encoding = "base64",
        Size = 4,
        Source = "Test Server",
        SiteId = 1,
        TenantId = 2,
        Timestamp = "2026-01-01T00:00:00.000Z",
        QueuedAt = "2026-01-01T00:00:00.000Z"
    };

    // ── Empty queue behaviour ────────────────────────────────────────────────

    [Fact]
    public void GetAll_EmptyQueue_ReturnsEmptyList()
        => Assert.Empty(_queue.GetAll());

    [Fact]
    public void GetPendingCount_EmptyQueue_ReturnsZero()
        => Assert.Equal(0, _queue.GetPendingCount());

    // ── Enqueue / GetAll ─────────────────────────────────────────────────────

    [Fact]
    public void Enqueue_SingleEntry_RoundTrip()
    {
        _queue.Enqueue(MakeEntry("e1"));

        var all = _queue.GetAll();
        Assert.Single(all);
        Assert.Equal("e1", all[0].PendingFileId);
        Assert.Equal("e1.txt", all[0].Filename);
        Assert.Equal("srv1", all[0].ServerId);
    }

    [Fact]
    public void Enqueue_MultipleEntries_AllReturned()
    {
        _queue.Enqueue(MakeEntry("a"));
        _queue.Enqueue(MakeEntry("b"));
        _queue.Enqueue(MakeEntry("c"));

        Assert.Equal(3, _queue.GetAll().Count);
        Assert.Equal(3, _queue.GetPendingCount());
    }

    [Fact]
    public void Enqueue_PreservesAllFields()
    {
        var entry = new PendingFileEntry
        {
            PendingFileId = "full-test",
            ServerId = "server-99",
            Filename = "important.DAE",
            ContentBase64 = Convert.ToBase64String(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
            Sha256 = "deadbeef1234567890abcdef",
            Encoding = "base64",
            Size = 4,
            Source = "Plant Floor FTP",
            SiteId = 1978,
            TenantId = 1001,
            Timestamp = "2026-02-22T12:34:56.789Z",
            QueuedAt = "2026-02-22T12:34:57.000Z"
        };

        _queue.Enqueue(entry);
        var loaded = _queue.GetAll()[0];

        Assert.Equal(entry.PendingFileId, loaded.PendingFileId);
        Assert.Equal(entry.ServerId, loaded.ServerId);
        Assert.Equal(entry.Filename, loaded.Filename);
        Assert.Equal(entry.ContentBase64, loaded.ContentBase64);
        Assert.Equal(entry.Sha256, loaded.Sha256);
        Assert.Equal(entry.Encoding, loaded.Encoding);
        Assert.Equal(entry.Size, loaded.Size);
        Assert.Equal(entry.Source, loaded.Source);
        Assert.Equal(entry.SiteId, loaded.SiteId);
        Assert.Equal(entry.TenantId, loaded.TenantId);
        Assert.Equal(entry.Timestamp, loaded.Timestamp);
        Assert.Equal(entry.QueuedAt, loaded.QueuedAt);
    }

    // ── Remove ───────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_ExistingEntry_EmptiesQueue()
    {
        _queue.Enqueue(MakeEntry("to-remove"));
        _queue.Remove("to-remove");

        Assert.Empty(_queue.GetAll());
        Assert.Equal(0, _queue.GetPendingCount());
    }

    [Fact]
    public void Remove_NonExistent_NoException()
    {
        _queue.Remove("does-not-exist"); // must not throw
        Assert.Equal(0, _queue.GetPendingCount());
    }

    [Fact]
    public void Remove_Partial_RemainingEntriesIntact()
    {
        _queue.Enqueue(MakeEntry("keep"));
        _queue.Enqueue(MakeEntry("discard"));
        _queue.Remove("discard");

        var all = _queue.GetAll();
        Assert.Single(all);
        Assert.Equal("keep", all[0].PendingFileId);
    }

    [Fact]
    public void Remove_ThenEnqueue_CountCorrect()
    {
        _queue.Enqueue(MakeEntry("x"));
        _queue.Remove("x");
        _queue.Enqueue(MakeEntry("y"));

        Assert.Equal(1, _queue.GetPendingCount());
        Assert.Equal("y", _queue.GetAll()[0].PendingFileId);
    }

    // ── Corrupted file handling ───────────────────────────────────────────────

    [Fact]
    public void GetAll_CorruptedFile_SkippedAndDeleted()
    {
        _queue.Enqueue(MakeEntry("good"));

        // Write a broken JSON file directly into the queue directory
        var corruptPath = Path.Combine(_tempDir, "corrupt_entry.json");
        File.WriteAllText(corruptPath, "{ this is not valid json {{{{");

        var all = _queue.GetAll();

        Assert.Single(all);
        Assert.Equal("good", all[0].PendingFileId);
        Assert.False(File.Exists(corruptPath), "Corrupted file should be deleted by GetAll");
    }

    [Fact]
    public void GetAll_EmptyJsonFile_SkippedAndDeleted()
    {
        _queue.Enqueue(MakeEntry("valid"));

        var emptyPath = Path.Combine(_tempDir, "empty_entry.json");
        File.WriteAllText(emptyPath, "");

        var all = _queue.GetAll();
        Assert.Single(all);
        Assert.False(File.Exists(emptyPath), "Empty/null-deserializing file should be deleted");
    }

    // ── Multi-server isolation ────────────────────────────────────────────────

    [Fact]
    public void Enqueue_MultipleServers_AllReturnedByGetAll()
    {
        _queue.Enqueue(MakeEntry("file1", "serverA"));
        _queue.Enqueue(MakeEntry("file2", "serverB"));
        _queue.Enqueue(MakeEntry("file3", "serverA"));

        var all = _queue.GetAll();
        Assert.Equal(3, all.Count);
        Assert.Equal(2, all.Count(e => e.ServerId == "serverA"));
        Assert.Equal(1, all.Count(e => e.ServerId == "serverB"));
    }
}
