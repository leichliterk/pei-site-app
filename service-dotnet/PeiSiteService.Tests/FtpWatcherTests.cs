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

// ── ParseMdtmTimestamp ────────────────────────────────────────────────────────

public class ParseMdtmTimestampTests
{
    [Theory]
    [InlineData("213 20260115103045", "20260115103045")]
    [InlineData("213 19991231235959", "19991231235959")]
    [InlineData("213  20260115103045 ", "20260115103045")]   // extra whitespace
    public void ValidResponse_ReturnsTimestamp(string input, string expected)
        => Assert.Equal(expected, FtpWatcher.ParseMdtmTimestamp(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("500 Unknown command")]           // not supported
    [InlineData("550 No such file")]              // file not found
    [InlineData("213 2026011510304")]             // 13 digits (too short)
    [InlineData("213 202601151030456")]           // 15 digits (too long)
    [InlineData("213 2026011510304X")]            // non-digit character
    [InlineData("214 20260115103045")]            // wrong code
    public void InvalidOrUnsupported_ReturnsNull(string? input)
        => Assert.Null(FtpWatcher.ParseMdtmTimestamp(input));
}

// ── MdtmToIso8601 ────────────────────────────────────────────────────────────

public class MdtmToIso8601Tests
{
    [Theory]
    [InlineData("20260115103045", "2026-01-15T10:30:45.000Z")]
    [InlineData("19991231235959", "1999-12-31T23:59:59.000Z")]
    [InlineData("20260101000000", "2026-01-01T00:00:00.000Z")]
    public void ValidMdtm_ReturnsIso8601(string input, string expected)
        => Assert.Equal(expected, FtpWatcher.MdtmToIso8601(input));

    [Theory]
    [InlineData("2026-01-15T10:30:45.000Z")]   // already ISO 8601 — pass through
    [InlineData("not-a-date")]                  // garbage — pass through
    [InlineData("2026011510304")]               // 13 digits — pass through
    [InlineData("202601151030456")]             // 15 digits — pass through
    public void NonMdtm_ReturnsUnchanged(string input)
        => Assert.Equal(input, FtpWatcher.MdtmToIso8601(input));
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
            ModifiedAt = "20260222120000",
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
        Assert.Equal("2026-02-22T12:00:00.000Z", root.GetProperty("modifiedAt").GetString());
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
            ModifiedAt = "20260222120000",
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
            ModifiedAt = "20260222120000",
            QueuedAt = "2026-02-22T00:00:00.000Z"
        };

        var payload = FtpWatcher.CreatePayloadFromEntry(entry);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("ftp.plantfloor.local", doc.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public void EmptyModifiedAt_MapsToNullInPayload()
    {
        // When modifiedAt is unknown (e.g. server doesn't support MDTM or LIST timestamps),
        // the payload should send null rather than an empty string.
        var entry = new PendingFileEntry
        {
            Filename = "unknown.txt",
            ContentBase64 = "",
            Sha256 = "",
            ModifiedAt = "",
            QueuedAt = "2026-02-22T00:00:00.000Z"
        };

        var payload = FtpWatcher.CreatePayloadFromEntry(entry);
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("modifiedAt").ValueKind);
    }
}

// ── ParseListEntry ────────────────────────────────────────────────────────────

public class ParseListEntryTests
{
    // Windows CE / IIS format files

    [Theory]
    [InlineData("11-15-25  00:00AM       11786 001870_251114_000500.DAE", "001870_251114_000500.DAE", 11786L, "20251115000000")]
    [InlineData("01-15-26  10:30AM       4096 report.pdf", "report.pdf", 4096L, "20260115103000")]
    [InlineData("06-15-25  03:30PM       512 data.txt", "data.txt", 512L, "20250615153000")]
    [InlineData("12-31-25  11:59PM       1024 backup.zip", "backup.zip", 1024L, "20251231235900")]
    [InlineData("01-01-00  12:00PM       100 test.bin", "test.bin", 100L, "20000101120000")]
    public void WindowsCE_File_ReturnsEntry(string line, string expectedName, long expectedSize, string expectedModifiedAt)
    {
        var entry = FtpWatcher.ParseListEntry(line);
        Assert.NotNull(entry);
        Assert.Equal(expectedName, entry!.Name);
        Assert.Equal(expectedSize, entry.Size);
        Assert.Equal(expectedModifiedAt, entry.ModifiedAt);
    }

    [Theory]
    [InlineData("06-15-25  03:30PM       512 file with spaces.txt", "file with spaces.txt")]
    public void WindowsCE_FileWithSpaces_ReturnsCorrectName(string line, string expectedName)
    {
        var entry = FtpWatcher.ParseListEntry(line);
        Assert.NotNull(entry);
        Assert.Equal(expectedName, entry!.Name);
    }

    [Theory]
    [InlineData("01-15-26  10:30AM       <DIR>          FolderName")]
    [InlineData("11-15-25  00:00AM       <DIR>          Archive")]
    public void WindowsCE_Directory_ReturnsNull(string line)
        => Assert.Null(FtpWatcher.ParseListEntry(line));

    // Unix format files

    [Theory]
    [InlineData("-rw-r--r-- 1 user grp 11786 Jan 15 00:00 myfile.txt", "myfile.txt", 11786L)]
    [InlineData("-rwxr-xr-x 1 root root 1024 Feb  5 12:00 run.sh", "run.sh", 1024L)]
    [InlineData("-rw-r--r-- 1 user grp 512 Dec 31 23:59 archive.DAE", "archive.DAE", 512L)]
    public void Unix_File_ReturnsEntry(string line, string expectedName, long expectedSize)
    {
        var entry = FtpWatcher.ParseListEntry(line);
        Assert.NotNull(entry);
        Assert.Equal(expectedName, entry!.Name);
        Assert.Equal(expectedSize, entry.Size);
        Assert.NotNull(entry.ModifiedAt); // timestamp parsed from month/day/time
    }

    [Theory]
    [InlineData("-rw-r--r-- 1 user grp 11786 Jan 15 00:00 file with spaces.txt", "file with spaces.txt")]
    public void Unix_FileWithSpaces_ReturnsCorrectName(string line, string expectedName)
    {
        var entry = FtpWatcher.ParseListEntry(line);
        Assert.NotNull(entry);
        Assert.Equal(expectedName, entry!.Name);
    }

    [Theory]
    [InlineData("drwxr-xr-x  2 user group 4096 Jan 15 10:30 FolderName")]
    [InlineData("drwxrwxrwx  3 root root 4096 Dec 31 23:59 Archive")]
    public void Unix_Directory_ReturnsNull(string line)
        => Assert.Null(FtpWatcher.ParseListEntry(line));

    // Plain filename fallback (no metadata)

    [Theory]
    [InlineData("myfile.txt", "myfile.txt")]
    [InlineData("001870_251114_000500.DAE", "001870_251114_000500.DAE")]
    public void PlainName_ReturnEntryWithNullMetadata(string line, string expectedName)
    {
        var entry = FtpWatcher.ParseListEntry(line);
        Assert.NotNull(entry);
        Assert.Equal(expectedName, entry!.Name);
        Assert.Null(entry.Size);
        Assert.Null(entry.ModifiedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_ReturnsNull(string line)
        => Assert.Null(FtpWatcher.ParseListEntry(line));
}

// ── ParseWindowsCeDateTime ────────────────────────────────────────────────────

public class ParseWindowsCeDateTimeTests
{
    [Theory]
    [InlineData("11-15-25", "00:00AM", "20251115000000")]
    [InlineData("01-15-26", "10:30AM", "20260115103000")]
    [InlineData("06-15-25", "03:30PM", "20250615153000")]
    [InlineData("12-31-25", "11:59PM", "20251231235900")]
    [InlineData("01-01-00", "12:00PM", "20000101120000")]  // year 00 → 2000
    [InlineData("12-31-69", "12:00AM", "20691231000000")]  // year 69 → 2069 (< 70 threshold)
    [InlineData("06-15-25", "12:00AM", "20250615000000")]  // 12:00AM = midnight
    [InlineData("06-15-25", "12:30PM", "20250615123000")]  // 12:30PM = noon
    public void ValidInput_ReturnsTimestamp(string datePart, string timePart, string expected)
        => Assert.Equal(expected, FtpWatcher.ParseWindowsCeDateTime(datePart, timePart));

    [Theory]
    [InlineData("11-15", "00:00AM")]        // bad date (missing year)
    [InlineData("11-15-25", "00:00")]       // missing AM/PM
    [InlineData("11-15-25", "")]            // empty time
    [InlineData("not-a-date", "10:00AM")]   // unparseable date
    public void InvalidInput_ReturnsNull(string datePart, string timePart)
        => Assert.Null(FtpWatcher.ParseWindowsCeDateTime(datePart, timePart));
}

// ── ParseUnixDateTime ─────────────────────────────────────────────────────────

public class ParseUnixDateTimeTests
{
    [Theory]
    [InlineData("Jan", "15", "2024", "20240115000000")]
    [InlineData("Dec", "31", "2023", "20231231000000")]
    [InlineData("Feb", " 5", "2025", "20250205000000")]  // space-padded day
    public void WithYear_ReturnsTimestamp(string month, string day, string year, string expected)
        => Assert.Equal(expected, FtpWatcher.ParseUnixDateTime(month, day, year));

    [Theory]
    [InlineData("Jan", "15", "10:30", "103000")]   // partial: HH:MM:00
    [InlineData("Dec", "31", "23:59", "235900")]
    [InlineData("Feb", " 5", "00:00", "000000")]
    public void WithTime_UsesCurrentYearAndParsesTime(string month, string day, string time, string expectedHHMMSS)
    {
        var result = FtpWatcher.ParseUnixDateTime(month, day, time);
        Assert.NotNull(result);
        // Check year matches current UTC year
        Assert.StartsWith(DateTime.UtcNow.Year.ToString(), result);
        // Check the time portion (last 6 digits)
        Assert.EndsWith(expectedHHMMSS, result);
    }

    [Theory]
    [InlineData("Xxx", "15", "2024")]     // invalid month
    [InlineData("Jan", "XX", "2024")]     // invalid day
    [InlineData("Jan", "15", "ABCD")]     // invalid year
    public void InvalidInput_ReturnsNull(string month, string day, string yearOrTime)
        => Assert.Null(FtpWatcher.ParseUnixDateTime(month, day, yearOrTime));
}
