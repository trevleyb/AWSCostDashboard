using AWSCostDashboard.Models;
using Spectre.Console;

namespace AWSCostDashboard.Services;

public class DisplayService(CostAnalysisService analysisService) {
    public void ShowDashboard() {
        AnsiConsole.Clear();
        ShowHeader();
        ShowMonthComparisons();
        ShowServiceAccountSummary();
        ShowDayByDayComparison();
        ShowAccountBreakdown();
    }

    public void ShowHeader() {
        var rule = new Rule("[bold blue]AWS Cost Monitor[/]") {
            Justification = Justify.Center
        };

        AnsiConsole.Write(rule);
        
        var creditsStatus = analysisService.IncludeCredits 
            ? "[green]Including Credits (Net Cost)[/]" 
            : "[yellow]Excluding Credits (Gross Cost)[/]";
        
        AnsiConsole.MarkupLine($"[grey]Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}[/]  |  {creditsStatus}");
        
        // Show credits summary if excluding
        if (!analysisService.IncludeCredits) {
            var (mtdCredits, lastMonthCredits) = analysisService.GetCreditsSummary();
            if (mtdCredits > 0 || lastMonthCredits > 0) {
                AnsiConsole.MarkupLine($"[grey]Credits not shown: MTD ${mtdCredits:N2} | Last Month ${lastMonthCredits:N2}[/]");
            }
        }
        
        AnsiConsole.WriteLine();
    }

    public void ShowMonthComparisons() {
        var mtd = analysisService.GetMonthToDateComparison();
        var full = analysisService.GetFullMonthComparison();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Comparison")
            .AddColumn(new TableColumn("Current").RightAligned())
            .AddColumn(new TableColumn("Previous").RightAligned())
            .AddColumn(new TableColumn("Difference").RightAligned())
            .AddColumn(new TableColumn("% Change").RightAligned());

        AddComparisonRow(table, mtd);
        AddComparisonRow(table, full);

        AnsiConsole.Write(new Panel(table).Header("[bold]Month Comparisons[/]").Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();
    }

    public void ShowServiceComparison() {
        AnsiConsole.Clear();

        var thisMonth = analysisService.GetAccountSummariesThisMonth()
            .SelectMany(a => a.CostByService.Select(s => new { a.AccountId, Service = s.Key, Cost = s.Value }))
            .GroupBy(x => x.Service)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cost));

        var lastMonth = analysisService.GetAccountSummariesLastMonth()
            .SelectMany(a => a.CostByService.Select(s => new { a.AccountId, Service = s.Key, Cost = s.Value }))
            .GroupBy(x => x.Service)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cost));

        var allServices = thisMonth.Keys.Union(lastMonth.Keys).OrderBy(x => x).ToList();

        if (!allServices.Any()) {
            AnsiConsole.MarkupLine("[yellow]No service data available. Try refreshing first.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Service")
            .AddColumn(new TableColumn("This Month").RightAligned())
            .AddColumn(new TableColumn("Last Month").RightAligned())
            .AddColumn(new TableColumn("Difference").RightAligned())
            .AddColumn(new TableColumn("% Change").RightAligned());

        var rows = allServices.Select(service => {
            var current = thisMonth.GetValueOrDefault(service, 0);
            var previous = lastMonth.GetValueOrDefault(service, 0);
            var diff = current - previous;
            var pct = previous != 0 ? (diff / previous) * 100 : (current != 0 ? 100 : 0);
            return new { Service = service, Current = current, Previous = previous, Diff = diff, Pct = pct };
        }).OrderByDescending(x => x.Current).Take(20);

        foreach (var row in rows) {
            var diffColor = row.Diff >= 0 ? "red" : "green";
            var arrow = row.Diff >= 0 ? "↑" : "↓";

            table.AddRow(
                row.Service.Length > 40 ? row.Service[..37] + "..." : row.Service, 
                $"${row.Current:N2}", 
                $"${row.Previous:N2}", 
                row.Diff != 0 ? $"[{diffColor}]{arrow} ${Math.Abs(row.Diff):N2}[/]" : "[grey]-[/]", 
                row.Previous > 0 ? $"[{diffColor}]{row.Pct:+0.0;-0.0}%[/]" : "[grey]-[/]");
        }

        AnsiConsole.Write(new Panel(table)
            .Header("[bold]Service Comparison (Top 20 by Current Cost)[/]")
            .Border(BoxBorder.Rounded));
    }
    
    public void ShowServiceAccountSummary() {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var yesterday = today.AddDays(-1);
        var lastFullMonthStart = yesterday.AddDays(-29);
        var previousFullMonthEnd = lastFullMonthStart.AddDays(-1);
        var previousFullMonthStart = previousFullMonthEnd.AddDays(-29);

        // Show by Account first
        ShowSummaryTable(
            "Account Summary",
            analysisService.GetAccountComparison().ToList(),
            $"MTD ({thisMonthStart:MMM d} - {today:MMM d})",
            $"Last MTD ({lastMonthStart:MMM d} - {lastMonthStart.AddDays(today.Day - 1):MMM d})",
            $"Rolling 30d ({lastFullMonthStart:MMM d} - {yesterday:MMM d})",
            $"Prev 30d ({previousFullMonthStart:MMM d} - {previousFullMonthEnd:MMM d})"
        );

        // Show by Service
        ShowSummaryTable(
            "Service Summary (Top 20)",
            analysisService.GetServiceComparison().Take(20).ToList(),
            $"MTD ({thisMonthStart:MMM d} - {today:MMM d})",
            $"Last MTD ({lastMonthStart:MMM d} - {lastMonthStart.AddDays(today.Day - 1):MMM d})",
            $"Rolling 30d ({lastFullMonthStart:MMM d} - {yesterday:MMM d})",
            $"Prev 30d ({previousFullMonthStart:MMM d} - {previousFullMonthEnd:MMM d})"
        );
    }

    public void ShowSummaryTable(
        string title, 
        List<ServiceAccountSummary> items,
        string mtdLabel,
        string lastMtdLabel,
        string rolling30Label,
        string prev30Label) {
        
        if (!items.Any()) {
            AnsiConsole.MarkupLine($"[yellow]No data available for {title}.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Name")
            .AddColumn(new TableColumn(mtdLabel).RightAligned())
            .AddColumn(new TableColumn(lastMtdLabel).RightAligned())
            .AddColumn(new TableColumn("Diff %").RightAligned())
            .AddColumn(new TableColumn("").Centered())
            .AddColumn(new TableColumn(rolling30Label).RightAligned())
            .AddColumn(new TableColumn(prev30Label).RightAligned())
            .AddColumn(new TableColumn("Diff %").RightAligned())
            .AddColumn(new TableColumn("").Centered());

        foreach (var item in items) {
            var mtdColor = item.MtdIsUp ? "red" : "green";
            var mtdArrow = item.MtdIsUp ? "↑" : "↓";
            var fullColor = item.FullMonthIsUp ? "red" : "green";
            var fullArrow = item.FullMonthIsUp ? "↑" : "↓";

            // Truncate long names
            var displayName = item.Name.Length > 35 ? item.Name[..32] + "..." : item.Name;

            table.AddRow(
                displayName,
                $"${item.MtdCost:N2}",
                item.LastMonthSameDayCost > 0 ? $"${item.LastMonthSameDayCost:N2}" : "[grey]-[/]",
                item.LastMonthSameDayCost > 0 ? $"[{mtdColor}]{item.MtdDifferencePercent:+0.0;-0.0}%[/]" : "[grey]-[/]",
                item.LastMonthSameDayCost > 0 ? $"[{mtdColor}]{mtdArrow}[/]" : "[grey]-[/]",
                $"${item.LastFullMonthCost:N2}",
                item.PreviousFullMonthCost > 0 ? $"${item.PreviousFullMonthCost:N2}" : "[grey]-[/]",
                item.PreviousFullMonthCost > 0 ? $"[{fullColor}]{item.FullMonthDifferencePercent:+0.0;-0.0}%[/]" : "[grey]-[/]",
                item.PreviousFullMonthCost > 0 ? $"[{fullColor}]{fullArrow}[/]" : "[grey]-[/]"
            );
        }

        // Add totals row
        var totalMtd = items.Sum(x => x.MtdCost);
        var totalLastSameDay = items.Sum(x => x.LastMonthSameDayCost);
        var totalLastFull = items.Sum(x => x.LastFullMonthCost);
        var totalPrevFull = items.Sum(x => x.PreviousFullMonthCost);

        var totalMtdDiff = totalLastSameDay != 0 ? ((totalMtd - totalLastSameDay) / totalLastSameDay) * 100 : 0;
        var totalFullDiff = totalPrevFull != 0 ? ((totalLastFull - totalPrevFull) / totalPrevFull) * 100 : 0;

        var totalMtdColor = totalMtd >= totalLastSameDay ? "red" : "green";
        var totalMtdArrow = totalMtd >= totalLastSameDay ? "↑" : "↓";
        var totalFullColor = totalLastFull >= totalPrevFull ? "red" : "green";
        var totalFullArrow = totalLastFull >= totalPrevFull ? "↑" : "↓";

        table.AddEmptyRow();
        table.AddRow(
            "[bold]TOTAL[/]",
            $"[bold]${totalMtd:N2}[/]",
            $"[bold]${totalLastSameDay:N2}[/]",
            $"[bold {totalMtdColor}]{totalMtdDiff:+0.0;-0.0}%[/]",
            $"[bold {totalMtdColor}]{totalMtdArrow}[/]",
            $"[bold]${totalLastFull:N2}[/]",
            $"[bold]${totalPrevFull:N2}[/]",
            $"[bold {totalFullColor}]{totalFullDiff:+0.0;-0.0}%[/]",
            $"[bold {totalFullColor}]{totalFullArrow}[/]"
        );

        AnsiConsole.Write(new Panel(table).Header($"[bold]{title}[/]").Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();
    }

    private void AddComparisonRow(Table table, CostComparison comparison) {
        var diffColor = comparison.Difference >= 0 ? "red" : "green";
        var arrow = comparison.Difference >= 0 ? "↑" : "↓";

        table.AddRow(
            comparison.Label, 
            $"${comparison.CurrentPeriod:N2}", 
            $"${comparison.PreviousPeriod:N2}", 
            $"[{diffColor}]{arrow} ${Math.Abs(comparison.Difference):N2}[/]", 
            $"[{diffColor}]{comparison.PercentageChange:+0.0;-0.0}%[/]");
    }

    public void ShowDayByDayComparison() {
        var days = analysisService.GetDayByDayComparison().ToList();

        if (!days.Any()) {
            AnsiConsole.MarkupLine("[yellow]No daily data available yet.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Day")
            .AddColumn(new TableColumn("This Month").RightAligned())
            .AddColumn(new TableColumn("Last Month").RightAligned())
            .AddColumn(new TableColumn("Difference").RightAligned())
            .AddColumn(new TableColumn("% Change").RightAligned());

        decimal runningThisMonth = 0;
        decimal runningLastMonth = 0;

        foreach (var day in days) {
            runningThisMonth += day.ThisMonth;
            runningLastMonth += day.LastMonth;

            var diffColor = day.Difference >= 0 ? "red" : "green";
            var arrow = day.Difference >= 0 ? "↑" : "↓";

            table.AddRow(
                day.DayOfMonth.ToString(), 
                day.ThisMonth > 0 ? $"${day.ThisMonth:N2}" : "[grey]-[/]", 
                day.LastMonth > 0 ? $"${day.LastMonth:N2}" : "[grey]-[/]", 
                day.Difference != 0 ? $"[{diffColor}]{arrow} ${Math.Abs(day.Difference):N2}[/]" : "[grey]-[/]", 
                day.LastMonth > 0 ? $"[{diffColor}]{day.PercentageChange:+0.0;-0.0}%[/]" : "[grey]-[/]");
        }

        // Add totals row
        table.AddEmptyRow();
        var totalDiff = runningThisMonth - runningLastMonth;
        var totalPct = runningLastMonth != 0 ? (totalDiff / runningLastMonth) * 100 : 0;
        var totalDiffColor = totalDiff >= 0 ? "red" : "green";
        var totalArrow = totalDiff >= 0 ? "↑" : "↓";

        table.AddRow(
            "[bold]Total[/]", 
            $"[bold]${runningThisMonth:N2}[/]", 
            $"[bold]${runningLastMonth:N2}[/]", 
            $"[bold {totalDiffColor}]{totalArrow} ${Math.Abs(totalDiff):N2}[/]", 
            $"[bold {totalDiffColor}]{totalPct:+0.0;-0.0}%[/]");

        AnsiConsole.Write(new Panel(table).Header("[bold]Day-by-Day Comparison (This Month vs Last Month)[/]").Border(BoxBorder.Rounded));
        AnsiConsole.WriteLine();
    }

    public void ShowAccountBreakdown() {
        var accounts = analysisService.GetAccountSummariesThisMonth().ToList();

        if (!accounts.Any()) {
            AnsiConsole.MarkupLine("[yellow]No account data available yet.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Account")
            .AddColumn(new TableColumn("Total (MTD)").RightAligned())
            .AddColumn("Top Services");

        foreach (var account in accounts.Take(10)) {
            var topServices = account.CostByService
                .OrderByDescending(x => x.Value)
                .Take(3)
                .Select(x => $"{x.Key}: ${x.Value:N2}");

            table.AddRow(
                $"[bold]{account.AccountName}[/]\n[grey]{account.AccountId}[/]", 
                $"${account.TotalCost:N2}", 
                string.Join("\n", topServices));
        }

        AnsiConsole.Write(new Panel(table).Header("[bold]Account Breakdown (Top 10 by Cost)[/]").Border(BoxBorder.Rounded));
    }

    public void ShowRefreshProgress(string message) {
        AnsiConsole.MarkupLine($"[blue]→[/] {message}");
    }

    public void ShowError(string message) {
        AnsiConsole.MarkupLine($"[red]✗[/] {message}");
    }

    public void ShowSuccess(string message) {
        AnsiConsole.MarkupLine($"[green]✓[/] {message}");
    }
}