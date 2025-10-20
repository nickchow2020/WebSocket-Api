namespace WebSocketApi.Models;

public class DashboardData
{
    public DateTime Timestamp { get; set; }
    public List<MetricItem> Metrics { get; set; } = new();
    public List<TableRow> TableData { get; set; } = new();
}

public class MetricItem
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public double MaxValue { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class TableRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTime LastUpdated { get; set; }
}
