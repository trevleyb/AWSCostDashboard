namespace AWSCostDashboard.Models;

public record DayComparison(
    int DayOfMonth,
    decimal ThisMonth,
    decimal LastMonth,
    decimal Difference,
    decimal PercentageChange
);