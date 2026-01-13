namespace AWSCostDashboard.Models;

public record ServiceAccountSummary(
    string Name,
    decimal MtdCost,
    decimal LastMonthSameDayCost,
    decimal MtdDifferencePercent,
    bool MtdIsUp,
    decimal LastFullMonthCost,
    decimal PreviousFullMonthCost,
    decimal FullMonthDifferencePercent,
    bool FullMonthIsUp
);
