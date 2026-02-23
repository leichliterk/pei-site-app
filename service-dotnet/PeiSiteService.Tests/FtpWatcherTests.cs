using System.Text.Json;
using PeiSiteService.Models;
using PeiSiteService.Services;
using Xunit;

namespace PeiSiteService.Tests;

// ── ParseNlstLine ────────────────────────────────────────────────────────────

public class ParseNlstLineTests
{
    // Plain filenames (most FTP servers just return the bare name)

    [Theory]
    [InlineData("myfile.txt", "myfile.txt")]
    [InlineData("001870_251114_000500.DAE", "001870_251114_000500.DAE")]
    [InlineData("report 2026.pdf", "report 2026.pdf")]
    [InlineData("  archive.txt  ", "archive.txt")]   // trimmed; 'a' not a Unix prefix
    public void PlainName_ReturnsName(string input, string expected)
        => Assert.Equal(expected, FtpWatcher.ParseNlstLine(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_ReturnsNull(string input)
        => Assert.Null(FtpWatcher.ParseNlstLine(input));

    // Windows CE / IIS format: "MM-DD-YY  HH:MMAM       size  name"

    [Theory]
    [InlineData("11-15-25  00:00AM       11786 001870_251114_000500.DAE", "001870_251114_000500.DAE")]
    [InlineData("12-31-25  11:59PM       4096 report.pdf", "report.pdf")]
    [InlineData("01-01-26  08:00AM       1024 upload.txt", "upload.txt")]
    [InlineData("06-15-25  03:30PM       512 file with spaces.txt", "file with spaces.txt")]
    public void WindowsCE_File_ReturnsFilename(string input, string expected)
        => Assert.Equal(expected, FtpWatcher.ParseNlstLine(input));

    [Theory]
    [InlineData("01-15-26  10:30AM       <DIR>          FolderName")]
    [InlineData("11-15-25  00:00AM       <DIR>          Archive")]
    [InlineData("03-01-26  09:00AM       <DIR>          Backup Files")]
    public void WindowsCE_Directory_ReturnsNull(string input)
        => Assert.Null(FtpWatcher.ParseNlstLine(input));

    // Unix format: "-rw-... 1 user grp size Mon DD HH:MM name"

    [Theory]
    [InlineData("-rw-r--r-- 1 user grp 11786 Jan 15 00:00 myfile.txt", "myfile.txt")]
    [InlineData("-rwxr-xr-x 1 root root 1024 Feb  5 12:00 run.sh", "run.sh")]
    [InlineData("-rw-r--r-- 1 user grp 11786 Jan 15 00:00 file with spaces.txt", "file with spaces.txt")]
    [InlineData("-rw-r--r-- 1 user grp 512 Dec 31 23:59 001870_251114_000500.DAE", "001870_251114_000500.DAE")]
    public void Unix_File_ReturnsFilename(string input, string expected)
        => Assert.Equal(expected, FtpWatcher.ParseNlstLine(input));

    [Theory]
    [InlineData("drwxr-xr-x  2 user group 4096 Jan 15 10:30 FolderName")]
    [InlineData("drwxrwxrwx  3 root root 4096 Dec 31 23:59 Archive")]
    public void Unix_Directory_ReturnsNull(string input)
        => Assert.Null(FtpWatcher.ParseNlstLine(input));

    [Theory]
    [InlineData("lrwxrwxrwx 1 root root 7 Jan 15 2024 mylink -> target", "mylink -> target")]
    public void Unix_Symlink_ReturnsName(string input, string expected)
        => Assert.Equal(expected, FtpWatcher.ParseNlstLine(input));
}

// ── CreatePayloadFromEntry ────────────────────────────────────────────────────

public class CreatePayloadFromEntryTests
{
    [Fact]
    public void AllFields_MappedToPayload()
    {
        var entry = new PendingFileEntry
        {
            PendingFileId = "srv1_20260222_abc",
            ServerId = "srv1",
            Filename = "report.DAE",
            ContentBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }),
            Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Encoding = "base64",
            Size = 3,
            Source = "Plant Floor FTP",
            SiteId = 1978,
            TenantId = 1001,
            Timestamp = "2026-02-22T00:00:00.000Z",
            QueuedAt = "2026-02-22T00:00:00.000Z"
        };

        var payload = FtpWatcher.CreatePayloadFromEntry(entry);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("report.DAE", root.GetProperty("filename").GetString());
        Assert.Equal(entry.ContentBase64, root.GetProperty("content").GetString());
        Assert.Equal(entry.Sha256, root.GetProperty("sha256").GetString());
        Assert.Equal("base64", root.GetProperty("encoding").GetString());
        Assert.Equal(3L, root.GetProperty("size").GetInt64());
        Assert.Equal("Plant Floor FTP", root.GetProperty("source").GetString());
        Assert.Equal(1978, root.GetProperty("siteId").GetInt32());
        Assert.Equal(1001, root.GetProperty("tenantId").GetInt32());
        Assert.Equal("2026-02-22T00:00:00.000Z", root.GetProperty("timestamp").GetString());
    }

    [Fact]
    public void NoPassword_OrInternalIds_InPayload()
    {
        var entry = new PendingFileEntry
        {
            PendingFileId = "should-not-appear",
            ServerId = "also-not-appear",
            Filename = "test.txt",
            ContentBase64 = "",
            Sha256 = "",
            Timestamp = "2026-02-22T00:00:00.000Z",
            QueuedAt = "2026-02-22T00:00:00.000Z"
        };

        var payload = FtpWatcher.CreatePayloadFromEntry(entry);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Internal queue fields must not leak into the wire payload
        Assert.False(root.TryGetProperty("pendingFileId", out _), "pendingFileId should not be in payload");
        Assert.False(root.TryGetProperty("serverId", out _), "serverId should not be in payload");
        Assert.False(root.TryGetProperty("queuedAt", out _), "queuedAt should not be in payload");
    }

    [Fact]
    public void Source_EmptyName_HostUsedAsSource()
    {
        // When FtpServerConfig.Name is empty, ForwardFile sets Source = FtpHost.
        // Verify CreatePayloadFromEntry passes that source value through unchanged.
        var entry = new PendingFileEntry
        {
            Source = "ftp.plantfloor.local", // host used because Name was empty
            Filename = "data.txt",
            ContentBase64 = "",
            Sha256 = "",
            Timestamp = "2026-02-22T00:00:00.000Z",
            QueuedAt = "2026-02-22T00:00:00.000Z"
        };

        var payload = FtpWatcher.CreatePayloadFromEntry(entry);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("ftp.plantfloor.local", doc.RootElement.GetProperty("source").GetString());
    }
}
