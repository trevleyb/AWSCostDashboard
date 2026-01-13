namespace AWSCostDashboard;

public class AwsSettings
{
    public string Profile { get; set; } = "default";
    public string Region { get; set; } = "us-east-1";
    public bool UseSso { get; set; } = true;
}

public class AppSettings
{
    public AwsSettings Aws { get; set; } = new();
    public string DatabasePath { get; set; } = "costs.db";
    public int RefreshIntervalMinutes { get; set; } = 60;
    public int FullSyncDays { get; set; } = 90;
    public bool RefreshOnStartup { get; set; } = true;
    public bool ShowCreditsByDefault { get; set; } = false;
}
