using AWSCostDashboard.Models;
using AWSCostDashboard.Repository;

namespace AWSCostDashboard.Services;

public class CostAnalysisService(CostRepository repository) {
    /// <summary>
    /// When true, credits are included in cost calculations (net cost).
    /// When false, only actual spend is shown (gross cost).
    /// </summary>
    public bool IncludeCredits { get; set; } = true;

    public CostComparison GetMonthToDateComparison() {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayOfMonth = today.Day;

        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var lastMonthSameDay = new DateOnly(
            lastMonthStart.Year, 
            lastMonthStart.Month, 
            Math.Min(dayOfMonth, DateTime.DaysInMonth(lastMonthStart.Year, lastMonthStart.Month)));

        var thisMonthTotal = repository.GetTotalForDateRange(thisMonthStart, today, IncludeCredits);
        var lastMonthTotal = repository.GetTotalForDateRange(lastMonthStart, lastMonthSameDay, IncludeCredits);

        return CreateComparison("Month-to-Date", thisMonthTotal, lastMonthTotal);
    }

    public CostComparison GetFullMonthComparison() {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var lastMonthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var lastMonthEnd = new DateOnly(today.Year, today.Month, 1).AddDays(-1);

        var previousMonthStart = lastMonthStart.AddMonths(-1);
        var previousMonthEnd = lastMonthStart.AddDays(-1);

        var lastMonthTotal = repository.GetTotalForDateRange(lastMonthStart, lastMonthEnd, IncludeCredits);
        var previousMonthTotal = repository.GetTotalForDateRange(previousMonthStart, previousMonthEnd, IncludeCredits);

        return CreateComparison($"{lastMonthStart:MMMM} vs {previousMonthStart:MMMM}", lastMonthTotal, previousMonthTotal);
    }

    public IEnumerable<DayComparison> GetDayByDayComparison() {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);

        var thisMonthTotals = repository.GetDailyTotals(thisMonthStart, today, IncludeCredits)
            .ToDictionary(x => x.Date.Day, x => x.Total);

        var lastMonthEnd = thisMonthStart.AddDays(-1);
        var lastMonthTotals = repository.GetDailyTotals(lastMonthStart, lastMonthEnd, IncludeCredits)
            .ToDictionary(x => x.Date.Day, x => x.Total);

        var maxDay = Math.Max(
            thisMonthTotals.Keys.DefaultIfEmpty(0).Max(), 
            lastMonthTotals.Keys.DefaultIfEmpty(0).Max());

        for (var day = 1; day <= maxDay; day++) {
            var thisMonth = thisMonthTotals.GetValueOrDefault(day, 0);
            var lastMonth = lastMonthTotals.GetValueOrDefault(day, 0);
            var diff = thisMonth - lastMonth;
            var pct = lastMonth != 0 ? (diff / lastMonth) * 100 : (thisMonth != 0 ? 100 : 0);

            yield return new DayComparison(day, thisMonth, lastMonth, diff, pct);
        }
    }

    public IEnumerable<AccountSummary> GetAccountSummariesThisMonth() {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);

        return repository.GetAccountSummaries(thisMonthStart, today, IncludeCredits);
    }

    public IEnumerable<AccountSummary> GetAccountSummariesLastMonth() {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var lastMonthEnd = thisMonthStart.AddDays(-1);

        return repository.GetAccountSummaries(lastMonthStart, lastMonthEnd, IncludeCredits);
    }

    /// <summary>
    /// Gets service/account summary with MTD vs last month same day, and rolling full month comparisons
    /// </summary>
    public IEnumerable<ServiceAccountSummary> GetServiceAccountComparison(bool byService = true) {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dayOfMonth = today.Day;

        // MTD: 1st of this month to today
        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);

        // Last month same day: 1st of last month to same day of last month
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var lastMonthSameDay = new DateOnly(
            lastMonthStart.Year, 
            lastMonthStart.Month, 
            Math.Min(dayOfMonth, DateTime.DaysInMonth(lastMonthStart.Year, lastMonthStart.Month)));

        // Last full month: rolling 30 days back from yesterday
        var yesterday = today.AddDays(-1);
        var lastFullMonthStart = yesterday.AddDays(-29);

        // Previous full month: the 30 days before that
        var previousFullMonthEnd = lastFullMonthStart.AddDays(-1);
        var previousFullMonthStart = previousFullMonthEnd.AddDays(-29);

        // Get costs grouped by service or account
        var mtdCosts = GetCostsByGrouping(thisMonthStart, today, byService);
        var lastMonthSameDayCosts = GetCostsByGrouping(lastMonthStart, lastMonthSameDay, byService);
        var lastFullMonthCosts = GetCostsByGrouping(lastFullMonthStart, yesterday, byService);
        var previousFullMonthCosts = GetCostsByGrouping(previousFullMonthStart, previousFullMonthEnd, byService);

        // Get all unique names
        var allNames = mtdCosts.Keys
            .Union(lastMonthSameDayCosts.Keys)
            .Union(lastFullMonthCosts.Keys)
            .Union(previousFullMonthCosts.Keys)
            .OrderBy(x => x);

        foreach (var name in allNames) {
            var mtd = mtdCosts.GetValueOrDefault(name, 0);
            var lastSameDay = lastMonthSameDayCosts.GetValueOrDefault(name, 0);
            var lastFull = lastFullMonthCosts.GetValueOrDefault(name, 0);
            var prevFull = previousFullMonthCosts.GetValueOrDefault(name, 0);

            var mtdDiff = lastSameDay != 0 ? ((mtd - lastSameDay) / lastSameDay) * 100 : (mtd != 0 ? 100 : 0);
            var fullDiff = prevFull != 0 ? ((lastFull - prevFull) / prevFull) * 100 : (lastFull != 0 ? 100 : 0);

            yield return new ServiceAccountSummary(
                name,
                mtd,
                lastSameDay,
                mtdDiff,
                mtd >= lastSameDay,
                lastFull,
                prevFull,
                fullDiff,
                lastFull >= prevFull
            );
        }
    }

    /// <summary>
    /// Gets service summary ordered by MTD cost descending
    /// </summary>
    public IEnumerable<ServiceAccountSummary> GetServiceComparison() {
        return GetServiceAccountComparison(byService: true)
            .OrderByDescending(x => x.MtdCost);
    }

    /// <summary>
    /// Gets account summary ordered by MTD cost descending
    /// </summary>
    public IEnumerable<ServiceAccountSummary> GetAccountComparison() {
        return GetServiceAccountComparison(byService: false)
            .OrderByDescending(x => x.MtdCost);
    }

    /// <summary>
    /// Gets credits summary for display
    /// </summary>
    public (decimal MtdCredits, decimal LastMonthCredits) GetCreditsSummary() {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var lastMonthEnd = thisMonthStart.AddDays(-1);

        var mtdCredits = repository.GetCreditsForDateRange(thisMonthStart, today);
        var lastMonthCredits = repository.GetCreditsForDateRange(lastMonthStart, lastMonthEnd);

        return (mtdCredits, lastMonthCredits);
    }

    private Dictionary<string, decimal> GetCostsByGrouping(DateOnly from, DateOnly to, bool byService) {
        var costs = repository.GetCostsForDateRange(from, to, IncludeCredits);

        if (byService) {
            return costs
                .GroupBy(c => c.Service)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Cost));
        } else {
            return costs
                .GroupBy(c => c.AccountName)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Cost));
        }
    }

    private static CostComparison CreateComparison(string label, decimal current, decimal previous) {
        var diff = current - previous;
        var pct = previous != 0 ? (diff / previous) * 100 : (current != 0 ? 100 : 0);

        return new CostComparison(label, current, previous, diff, pct);
    }
}