using WebSocketApi.Models;

namespace WebSocketApi.Services;

public class DashboardDataService
{
    private readonly Random _random = new();

    public DashboardData GenerateMockData()
    {
        return new DashboardData
        {
            Timestamp = DateTime.UtcNow,
            Metrics = GenerateMetrics(),
            TableData = GenerateTableData()
        };
    }

    private List<MetricItem> GenerateMetrics()
    {
        return new List<MetricItem>
        {
            new MetricItem
            {
                Name = "CPU Usage",
                Value = _random.Next(20, 95),
                MaxValue = 100,
                Unit = "%"
            },
            new MetricItem
            {
                Name = "Memory Usage",
                Value = _random.Next(30, 85),
                MaxValue = 100,
                Unit = "%"
            },
            new MetricItem
            {
                Name = "Disk Space",
                Value = _random.Next(40, 90),
                MaxValue = 100,
                Unit = "%"
            },
            new MetricItem
            {
                Name = "Network Traffic",
                Value = _random.Next(10, 100),
                MaxValue = 100,
                Unit = "Mbps"
            },
            new MetricItem
            {
                Name = "Active Connections",
                Value = _random.Next(50, 500),
                MaxValue = 1000,
                Unit = "connections"
            }
        };
    }

    private List<TableRow> GenerateTableData()
    {
        var statuses = new[] { "Active", "Pending", "Completed", "Error" };
        var names = new[] { "Task Alpha", "Task Beta", "Task Gamma", "Task Delta", "Task Epsilon",
                           "Task Zeta", "Task Eta", "Task Theta", "Task Iota", "Task Kappa" };

        var rows = new List<TableRow>();
        for (int i = 0; i < 10; i++)
        {
            rows.Add(new TableRow
            {
                Id = i + 1,
                Name = names[i],
                Status = statuses[_random.Next(statuses.Length)],
                Value = Math.Round(_random.NextDouble() * 1000, 2),
                LastUpdated = DateTime.UtcNow.AddMinutes(-_random.Next(0, 60))
            });
        }

        return rows;
    }
}
