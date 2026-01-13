namespace AWSCostDashboard.Models;

public record AccountSummary(
    string AccountId,
    string AccountName,
    decimal TotalCost,
    Dictionary<string, decimal> CostByService
);
