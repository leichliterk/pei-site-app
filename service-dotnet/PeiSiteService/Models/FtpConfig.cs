namespace PeiSiteService.Models;

public class FtpServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string FtpHost { get; set; } = "";
    public string FtpPath { get; set; } = "/";
    public int FtpPollInterval { get; set; } = 60;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class FtpConfig
{
    public bool FtpEnabled { get; set; }
    public List<FtpServerConfig> Servers { get; set; } = new();
}
