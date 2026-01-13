# AWS Cost Monitor

A .NET console application for monitoring AWS costs across multiple accounts. Runs in the background on macOS and provides day-by-day cost comparisons.

## Features

- **Day-by-Day Comparison**: Compare costs for each day this month vs the same day last month
- **Month-to-Date**: Compare MTD spending this month vs last month
- **Account Breakdown**: See costs broken down by account with top services
- **Service Comparison**: Compare service costs between months
- **Automatic Refresh**: Configurable auto-refresh interval
- **Local Database**: SQLite storage for offline access and historical data
- **AWS SSO Support**: Works with AWS SSO profiles

## Requirements

- .NET 8.0 SDK
- AWS credentials configured (SSO or standard credentials)
- Cost Explorer API access (requires appropriate IAM permissions)

## AWS Permissions Required

Your AWS credentials need the following permissions:

```json
{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Effect": "Allow",
            "Action": [
                "ce:GetCostAndUsage",
                "ce:GetDimensionValues"
            ],
            "Resource": "*"
        }
    ]
}
```

## Configuration

Edit `appsettings.json`:

```json
{
  "Aws": {
    "Profile": "your-sso-profile",
    "Region": "us-east-1",
    "UseSso": true
  },
  "DatabasePath": "costs.db",
  "RefreshIntervalMinutes": 60
}
```

### Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `Aws.Profile` | AWS credentials profile name | `default` |
| `Aws.Region` | AWS region for Cost Explorer API | `us-east-1` |
| `Aws.UseSso` | Whether using AWS SSO | `true` |
| `DatabasePath` | Path to SQLite database | `costs.db` |
| `RefreshIntervalMinutes` | Auto-refresh interval (0 to disable) | `60` |

### Environment Variables

You can also configure via environment variables with the `AWSCOST_` prefix:

```bash
export AWSCOST_Aws__Profile=my-profile
export AWSCOST_Aws__Region=eu-west-1
```

## Usage

### Interactive Mode

```bash
dotnet run
```

Commands:
- `R` - Refresh data from AWS
- `F` - Full sync (last 60 days)
- `D` - Show dashboard
- `A` - View account details
- `C` - Compare services
- `Q` - Quit

### Command Line Options

```bash
# Quick refresh and display
dotnet run -- --refresh
dotnet run -- -r

# Full 60-day sync
dotnet run -- --full-sync
```

## Running in Background

### macOS launchd

Create `~/Library/LaunchAgents/com.awscostmonitor.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.awscostmonitor</string>
    <key>ProgramArguments</key>
    <array>
        <string>/usr/local/share/dotnet/dotnet</string>
        <string>/path/to/AwsCostMonitor/bin/Release/net8.0/AwsCostMonitor.dll</string>
        <string>--refresh</string>
    </array>
    <key>StartInterval</key>
    <integer>3600</integer>
    <key>StandardOutPath</key>
    <string>/tmp/awscostmonitor.log</string>
    <key>StandardErrorPath</key>
    <string>/tmp/awscostmonitor.error.log</string>
</dict>
</plist>
```

Load with:
```bash
launchctl load ~/Library/LaunchAgents/com.awscostmonitor.plist
```

## Data Storage

Cost data is stored in a local SQLite database (`costs.db`). The database includes:

- Daily costs by account and service
- Account name mappings
- Historical data for trend analysis

## Notes

- AWS Cost Explorer data is typically delayed by 24-48 hours
- The first sync fetches 60 days of historical data
- Subsequent syncs fetch missing days plus the last 3 days (to catch updates)
- Cost Explorer API has a limit of 5 calls per second

## Project Structure

```
AwsCostMonitor/
├── Program.cs                    # Main entry point and interactive menu
├── Configuration/
│   └── AppSettings.cs           # Configuration models
├── Data/
│   └── CostRepository.cs        # SQLite data access
├── Models/
│   ├── CostComparison.cs        # Comparison DTOs
│   └── DailyCost.cs             # Daily cost record
├── Services/
│   ├── AwsCostService.cs        # AWS Cost Explorer client
│   ├── CostAnalysisService.cs   # Analysis and aggregation
│   ├── CostSyncService.cs       # Data synchronization
│   └── DisplayService.cs        # Console UI rendering
├── appsettings.json             # Configuration file
└── AwsCostMonitor.csproj        # Project file
```
