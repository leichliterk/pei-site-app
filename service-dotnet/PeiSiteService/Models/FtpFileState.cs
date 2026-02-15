namespace PeiSiteService.Models;

public class FtpFileState
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string ModifiedAt { get; set; } = "";
}

public class FtpState
{
    public Dictionary<string, FtpFileState> Files { get; set; } = new();
    public string? LastPoll { get; set; }
}
