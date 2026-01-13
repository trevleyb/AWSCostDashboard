namespace AWSCostDashboard.Models;

public record CostComparison(
    string Label,
    decimal CurrentPeriod,
    decimal PreviousPeriod,
    decimal Difference,
    decimal PercentageChange
);