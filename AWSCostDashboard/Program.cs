using AWSCostDashboard.Models;
using AWSCostDashboard.Repository;
using AWSCostDashboard.Services;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace AWSCostDashboard;

public class Program {
    private static readonly AppSettings _settings = new();
    private static CostRepository _repository = null!;
    private static CostAnalysisService _analysisService = null!;
    private static DisplayService _displayService = null!;
    private static CostSyncService _syncService = null!;
    private static CancellationTokenSource _cts = new();
    private static Timer? _autoRefreshTimer;

    public static async Task Main(string[] args) {
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            _cts.Cancel();
        };

        LoadConfiguration();
        InitializeServices();

        // Check for --no-credits flag
        if (args.Contains("--no-credits") || args.Contains("-nc") || !_settings.ShowCreditsByDefault) {
            _analysisService.IncludeCredits = false;
        }

        if (args.Contains("--refresh") || args.Contains("-r")) {
            await RefreshAndShowAsync();
            return;
        }

        if (args.Contains("--full-sync")) {
            await FullSyncAsync();
            return;
        }

        await RunInteractiveAsync();
    }

    private static void LoadConfiguration() {
        try {
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath)) throw new ArgumentException($"Couldn't find {configPath}.");
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables("AWSCOST_")
                .Build();
            config.Bind(_settings);
        } catch (Exception ex) {
            AnsiConsole.WriteLine("Load Config Error:");
            AnsiConsole.WriteException(ex);
            throw;
        }
    }

    private static void InitializeServices() {
        try {
            var dbPath = Path.IsPathRooted(_settings.DatabasePath) 
                ? _settings.DatabasePath 
                : Path.Combine(AppContext.BaseDirectory, _settings.DatabasePath);

            _repository = new CostRepository(dbPath);
            _analysisService = new CostAnalysisService(_repository);
            _displayService = new DisplayService(_analysisService);
            _syncService = new CostSyncService(_settings, _repository, _displayService);
        } catch (Exception ex) {
            AnsiConsole.WriteLine($"Initialise Service Error: {ex.Message}");
            throw;
        }
    }

    private static async Task RunInteractiveAsync() {
        if ( _settings.RefreshOnStartup) await RefreshAndShowAsync(); 
        //_displayService.ShowDashboard();
        _displayService.ShowMonthComparisons();

        if (_settings.RefreshIntervalMinutes > 0) {
            _autoRefreshTimer = new Timer(
                async void (_) => {
                    try {
                        await AutoRefreshAsync();
                    } catch (Exception ex) {
                        AnsiConsole.WriteLine($"Refresh Interval Error: {ex.Message}");
                    }
                }, 
                null, 
                TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes), 
                TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes));
        }

        while (!_cts.Token.IsCancellationRequested) {
            ShowMenu();

            var key = Console.ReadKey(true);

            switch (key.Key) {
            case ConsoleKey.R:
                await RefreshAndShowAsync();
                break;

            case ConsoleKey.F:
                await FullSyncAsync();
                break;

            case ConsoleKey.D:
                _displayService.ShowDashboard();
                break;

            case ConsoleKey.A:
                ShowAccountDetails();
                break;

            case ConsoleKey.D0:
                _displayService.ShowDashboard();
                break;

            case ConsoleKey.D1:
                _displayService.ShowHeader();
                _displayService.ShowMonthComparisons();
                break;

            case ConsoleKey.D2:
                _displayService.ShowHeader();
                _displayService.ShowServiceAccountSummary();
                break;

            case ConsoleKey.D3:
                _displayService.ShowHeader();
                _displayService.ShowDayByDayComparison();
                break;

            case ConsoleKey.D4:
                _displayService.ShowHeader();
                _displayService.ShowAccountBreakdown();
                break;

            case ConsoleKey.D5:
                _displayService.ShowHeader();
                _displayService.ShowServiceComparison();
                break;

            case ConsoleKey.X:
                ToggleCredits();
                break;

            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                await _cts.CancelAsync();
                break;
            }
        }

        Cleanup();
    }

    private static void ShowMenu() {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[grey]Commands[/]").LeftJustified());
        
        var creditsLabel = _analysisService.IncludeCredits ? "e[[X]]clude Credits" : "Include Credits [[X]]";
        AnsiConsole.MarkupLine($"[grey][[0]]Dashboard  [[1]]Month Compare  [[2]]Service Summary  [[3]]Day-by-Day  [[4]]Account Breakdown  [[5]]Service Compare [/]");
        AnsiConsole.MarkupLine($"[grey][[R]]efresh  [[F]]ull Sync  [[D]]ashboard  [[A]]ccounts  {creditsLabel}  [[Q]]uit[/]");
    }

    private static void ToggleCredits() {
        _analysisService.IncludeCredits = !_analysisService.IncludeCredits;
        
        var status = _analysisService.IncludeCredits ? "included" : "excluded";
        AnsiConsole.MarkupLine($"[blue]→[/] Credits now [bold]{status}[/]");
        
        _displayService.ShowDashboard();
    }

    private static async Task RefreshAndShowAsync() {
        try {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Refreshing cost data...", async ctx => { 
                    await _syncService.RefreshAsync(cancellationToken: _cts.Token); 
                });

            //_displayService.ShowDashboard();
        } catch (OperationCanceledException) {
            // Ignore
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    private static async Task FullSyncAsync() {
        try {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Performing full sync (last 60 days)...", async ctx => { 
                    await _syncService.RefreshAsync(forceFullSync: true, cancellationToken: _cts.Token); 
                });

            _displayService.ShowDashboard();
        } catch (OperationCanceledException) {
            // Ignore
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }
    }

    private static async Task AutoRefreshAsync() {
        try {
            await _syncService.RefreshAsync(cancellationToken: _cts.Token);
            _displayService.ShowDashboard();
            ShowMenu();
        } catch {
            // Silent fail for auto-refresh
        }
    }

    private static void ShowAccountDetails() {
        AnsiConsole.Clear();

        var accounts = _analysisService.GetAccountSummariesThisMonth().ToList();

        if (!accounts.Any()) {
            AnsiConsole.MarkupLine("[yellow]No account data available. Try refreshing first.[/]");
            return;
        }

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<AccountSummary>()
                .Title("Select an account to view details:")
                .PageSize(15)
                .UseConverter(a => $"{a.AccountName} ({a.AccountId}) - ${a.TotalCost:N2}")
                .AddChoices(accounts));

        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[bold]{selected.AccountName}[/]").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]Account ID: {selected.AccountId}[/]");
        AnsiConsole.MarkupLine($"[bold]Total MTD: ${selected.TotalCost:N2}[/]");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Service")
            .AddColumn(new TableColumn("Cost").RightAligned())
            .AddColumn(new TableColumn("% of Total").RightAligned());

        foreach (var service in selected.CostByService.OrderByDescending(x => x.Value)) {
            var pct = selected.TotalCost > 0 ? (service.Value / selected.TotalCost) * 100 : 0;
            table.AddRow(service.Key, $"${service.Value:N2}", $"{pct:F1}%");
        }

        AnsiConsole.Write(table);
    }

    private static void Cleanup() {
        _autoRefreshTimer?.Dispose();
        _repository.Dispose();
        _cts.Dispose();
    }
}