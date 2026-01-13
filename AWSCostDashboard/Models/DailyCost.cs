namespace AWSCostDashboard.Models;

public record DailyCost(
    DateOnly Date,
    string AccountId,
    string AccountName,
    string Service,
    decimal Cost,
    string Currency
);
