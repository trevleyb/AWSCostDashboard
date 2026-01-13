using AWSCostDashboard.Models;
using Microsoft.Data.Sqlite;

namespace AWSCostDashboard.Repository;

public class CostRepository : IDisposable {
    private readonly SqliteConnection _connection;

    public CostRepository(string databasePath) {
        var connectionString = new SqliteConnectionStringBuilder {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeDatabase();
    }

    private void InitializeDatabase() {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
                          CREATE TABLE IF NOT EXISTS daily_costs (
                              id INTEGER PRIMARY KEY AUTOINCREMENT,
                              date TEXT NOT NULL,
                              account_id TEXT NOT NULL,
                              account_name TEXT NOT NULL,
                              service TEXT NOT NULL,
                              cost REAL NOT NULL,
                              currency TEXT NOT NULL,
                              created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                              UNIQUE(date, account_id, service)
                          );

                          CREATE INDEX IF NOT EXISTS idx_daily_costs_date ON daily_costs(date);
                          CREATE INDEX IF NOT EXISTS idx_daily_costs_account ON daily_costs(account_id);
                          """;

        cmd.ExecuteNonQuery();
    }

    public DateOnly? GetLatestDate() {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT MAX(date) FROM daily_costs";
        var result = cmd.ExecuteScalar();

        if (result is DBNull || result is null)
            return null;

        return DateOnly.Parse((string)result);
    }

    public IEnumerable<DateOnly> GetMissingDates(DateOnly from, DateOnly to) {
        var existingDates = new HashSet<DateOnly>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT date FROM daily_costs WHERE date >= @from AND date <= @to";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            existingDates.Add(DateOnly.Parse(reader.GetString(0)));
        }

        var allDates = new List<DateOnly>();
        for (var date = from; date <= to; date = date.AddDays(1)) {
            if (!existingDates.Contains(date))
                allDates.Add(date);
        }

        return allDates;
    }

    public void UpsertCosts(IEnumerable<DailyCost> costs) {
        using var transaction = _connection.BeginTransaction();

        foreach (var cost in costs) {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                              INSERT INTO daily_costs (date, account_id, account_name, service, cost, currency)
                              VALUES (@date, @account_id, @account_name, @service, @cost, @currency)
                              ON CONFLICT(date, account_id, service) DO UPDATE SET
                                  account_name = excluded.account_name,
                                  cost = excluded.cost,
                                  currency = excluded.currency
                              """;

            cmd.Parameters.AddWithValue("@date", cost.Date.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@account_id", cost.AccountId);
            cmd.Parameters.AddWithValue("@account_name", cost.AccountName);
            cmd.Parameters.AddWithValue("@service", cost.Service);
            cmd.Parameters.AddWithValue("@cost", cost.Cost);
            cmd.Parameters.AddWithValue("@currency", cost.Currency);
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IEnumerable<DailyCost> GetCostsForDateRange(DateOnly from, DateOnly to, bool includeCredits = true) {
        using var cmd = _connection.CreateCommand();
        
        var creditFilter = includeCredits ? "" : "AND service NOT LIKE '%Credit%' AND cost >= 0";
        
        cmd.CommandText = $"""
                          SELECT date, account_id, account_name, service, cost, currency
                          FROM daily_costs
                          WHERE date >= @from AND date <= @to {creditFilter}
                          ORDER BY date, account_id, service
                          """;

        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            yield return new DailyCost(
                DateOnly.Parse(reader.GetString(0)), 
                reader.GetString(1), 
                reader.GetString(2), 
                reader.GetString(3), 
                (decimal)reader.GetDouble(4), 
                reader.GetString(5));
        }
    }

    public decimal GetTotalForDateRange(DateOnly from, DateOnly to, bool includeCredits = true) {
        using var cmd = _connection.CreateCommand();
        
        var creditFilter = includeCredits ? "" : "AND service NOT LIKE '%Credit%' AND cost >= 0";
        
        cmd.CommandText = $"SELECT COALESCE(SUM(cost), 0) FROM daily_costs WHERE date >= @from AND date <= @to {creditFilter}";
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    public IEnumerable<(DateOnly Date, decimal Total)> GetDailyTotals(DateOnly from, DateOnly to, bool includeCredits = true) {
        using var cmd = _connection.CreateCommand();
        
        var creditFilter = includeCredits ? "" : "AND service NOT LIKE '%Credit%' AND cost >= 0";
        
        cmd.CommandText = $"""
                          SELECT date, SUM(cost) as total
                          FROM daily_costs
                          WHERE date >= @from AND date <= @to {creditFilter}
                          GROUP BY date
                          ORDER BY date
                          """;

        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            yield return (DateOnly.Parse(reader.GetString(0)), (decimal)reader.GetDouble(1));
        }
    }

    public IEnumerable<AccountSummary> GetAccountSummaries(DateOnly from, DateOnly to, bool includeCredits = true) {
        var accounts = new Dictionary<string, AccountSummary>();

        using var cmd = _connection.CreateCommand();
        
        var creditFilter = includeCredits ? "" : "AND service NOT LIKE '%Credit%' AND cost >= 0";
        
        cmd.CommandText = $"""
                          SELECT account_id, account_name, service, SUM(cost) as total
                          FROM daily_costs
                          WHERE date >= @from AND date <= @to {creditFilter}
                          GROUP BY account_id, account_name, service
                          ORDER BY account_id, service
                          """;

        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) {
            var accountId = reader.GetString(0);
            var accountName = reader.GetString(1);
            var service = reader.GetString(2);
            var cost = (decimal)reader.GetDouble(3);

            if (!accounts.TryGetValue(accountId, out var summary)) {
                summary = new AccountSummary(accountId, accountName, 0, new Dictionary<string, decimal>());
                accounts[accountId] = summary;
            }

            summary.CostByService[service] = cost;
        }

        return accounts.Values
            .Select(a => a with { TotalCost = a.CostByService.Values.Sum() })
            .OrderByDescending(a => a.TotalCost);
    }

    /// <summary>
    /// Gets the total credits for a date range (returns as positive number)
    /// </summary>
    public decimal GetCreditsForDateRange(DateOnly from, DateOnly to) {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
                          SELECT COALESCE(SUM(ABS(cost)), 0) 
                          FROM daily_costs 
                          WHERE date >= @from AND date <= @to 
                          AND (service LIKE '%Credit%' OR cost < 0)
                          """;
        cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd"));

        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    public void Dispose() {
        _connection.Dispose();
    }
}