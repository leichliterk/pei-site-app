namespace PeiSiteService.Models;

public class FtpConfig
{
    public bool FtpEnabled { get; set; }
    public string FtpHost { get; set; } = "";
    public string FtpPath { get; set; } = "/";
    public int FtpPollInterval { get; set; } = 60;
}
