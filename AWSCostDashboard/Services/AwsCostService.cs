using Amazon;
using Amazon.CostExplorer;
using Amazon.CostExplorer.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using AWSCostDashboard.Models;

namespace AWSCostDashboard.Services;

public class AwsCostService : IDisposable {
    private readonly AmazonCostExplorerClient _client;
    private readonly Dictionary<string, string> _accountNames = new();

    public AwsCostService(AwsSettings settings) {
        var credentials = GetCredentials(settings);
        var config = new AmazonCostExplorerConfig {
            RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region),
        };

        _client = new AmazonCostExplorerClient(credentials, config);
    }

    private static AWSCredentials GetCredentials(AwsSettings settings) {
        var chain = new CredentialProfileStoreChain();

        if (chain.TryGetAWSCredentials(settings.Profile, out var credentials)) {
            return credentials;
        }

        // Fall back to environment variables or instance profile
        return FallbackCredentialsFactory.GetCredentials();
    }

    public async Task<IEnumerable<DailyCost>> GetCostsForDateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) {
        var costs = new List<DailyCost>();

        var request = new GetCostAndUsageRequest {
            TimePeriod = new DateInterval {
                Start = from.ToString("yyyy-MM-dd"),
                End = to.AddDays(1).ToString("yyyy-MM-dd") // End date is exclusive
            },
            Granularity = Granularity.DAILY,
            Metrics = new List<string> { "UnblendedCost" },
            GroupBy = new List<GroupDefinition> {
                new() { Type = GroupDefinitionType.DIMENSION, Key = "LINKED_ACCOUNT" },
                new() { Type = GroupDefinitionType.DIMENSION, Key = "SERVICE" }
            }
        };

        string? nextToken = null;

        do {
            request.NextPageToken = nextToken;
            var response = await _client.GetCostAndUsageAsync(request, cancellationToken);

            foreach (var result in response.ResultsByTime) {
                var date = DateOnly.Parse(result.TimePeriod.Start);

                foreach (var group in result.Groups) {
                    var accountId = group.Keys[0];
                    var service = group.Keys[1];
                    var amount = decimal.Parse(group.Metrics["UnblendedCost"].Amount);
                    var currency = group.Metrics["UnblendedCost"].Unit;

                    if (amount > 0) {
                        var accountName = await GetAccountNameAsync(accountId, cancellationToken);
                        costs.Add(new DailyCost(date, accountId, accountName, service, amount, currency));
                    }
                }
            }

            nextToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return costs;
    }

    private async Task<string> GetAccountNameAsync(string accountId, CancellationToken cancellationToken) {
        if (_accountNames.TryGetValue(accountId, out var name))
            return name;

        try {
            var request = new GetDimensionValuesRequest {
                TimePeriod = new DateInterval {
                    Start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)).ToString("yyyy-MM-dd"),
                    End = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
                },
                Dimension = Dimension.LINKED_ACCOUNT,
                Context = Context.COST_AND_USAGE
            };

            var response = await _client.GetDimensionValuesAsync(request, cancellationToken);

            foreach (var value in response.DimensionValues) {
                _accountNames[value.Value] = value.Attributes.GetValueOrDefault("description", value.Value);
            }

            return _accountNames.GetValueOrDefault(accountId, accountId);
        } catch {
            return accountId;
        }
    }

    public void Dispose() {
        _client.Dispose();
    }
}