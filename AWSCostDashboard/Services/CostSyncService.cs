using AWSCostDashboard.Repository;

namespace AWSCostDashboard.Services;

public class CostSyncService(AppSettings appSettings, CostRepository repository, DisplayService display) {
    private const int DaysToRefresh = -3;

    public async Task RefreshAsync(bool forceFullSync = false, CancellationToken cancellationToken = default) {
        display.ShowRefreshProgress("Connecting to AWS Cost Explorer...");

        using var costService = new AwsCostService(appSettings.Aws);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1); // Cost data is typically delayed by a day

        DateOnly syncFrom;

        if (forceFullSync) {
            syncFrom = today.AddDays(appSettings.FullSyncDays * -1);
            display.ShowRefreshProgress($"Full sync requested: fetching {syncFrom:yyyy-MM-dd} to {yesterday:yyyy-MM-dd}");
        } else {
            var latestDate = repository.GetLatestDate();
            if (latestDate.HasValue) {
                // Start from the day after the latest we have, but also refresh today-3 to catch updates
                var catchUpFrom = today.AddDays(DaysToRefresh);
                syncFrom = latestDate.Value < catchUpFrom ? latestDate.Value.AddDays(1) : catchUpFrom;
            } else {
                syncFrom = today.AddDays(appSettings.FullSyncDays * -1);
            }
        }

        var missingDates = repository.GetMissingDates(syncFrom, yesterday).ToList();
        if (missingDates.Count == 0 && !forceFullSync) syncFrom = today.AddDays(DaysToRefresh);

        display.ShowRefreshProgress($"Fetching costs from {syncFrom:yyyy-MM-dd} to {yesterday:yyyy-MM-dd}...");

        try {
            var costs = await costService.GetCostsForDateRangeAsync(syncFrom, yesterday, cancellationToken);
            var costList = costs.ToList();

            display.ShowRefreshProgress($"Retrieved {costList.Count} cost records");
            repository.UpsertCosts(costList);
            display.ShowSuccess($"Database updated with costs through {yesterday:yyyy-MM-dd}");
        } catch (Exception ex) {
            display.ShowError($"Failed to fetch costs: {ex.Message}");
            throw;
        }
    }
}