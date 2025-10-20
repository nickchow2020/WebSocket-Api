using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace WebSocketApi.Services;

public class WebSocketConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
    private readonly ILogger<WebSocketConnectionManager> _logger;

    public WebSocketConnectionManager(ILogger<WebSocketConnectionManager> logger)
    {
        _logger = logger;
    }

    public string AddConnection(WebSocket socket)
    {
        var connectionId = Guid.NewGuid().ToString();
        if (_connections.TryAdd(connectionId, socket))
        {
            _logger.LogInformation("WebSocket connection added. ID: {ConnectionId}. Total connections: {Count}",
                connectionId, _connections.Count);
            return connectionId;
        }

        _logger.LogWarning("Failed to add WebSocket connection. ID: {ConnectionId}", connectionId);
        throw new InvalidOperationException("Failed to add connection");
    }

    public bool RemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out _))
        {
            _logger.LogInformation("WebSocket connection removed. ID: {ConnectionId}. Total connections: {Count}",
                connectionId, _connections.Count);
            return true;
        }

        _logger.LogWarning("Failed to remove WebSocket connection. ID: {ConnectionId}", connectionId);
        return false;
    }

    public WebSocket? GetConnection(string connectionId)
    {
        _connections.TryGetValue(connectionId, out var socket);
        return socket;
    }

    public int GetConnectionCount()
    {
        return _connections.Count;
    }

    public IEnumerable<string> GetAllConnectionIds()
    {
        return _connections.Keys;
    }

    public async Task CloseAllConnectionsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Closing all WebSocket connections. Total: {Count}", _connections.Count);

        var closeTasks = _connections.Select(async kvp =>
        {
            try
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    await kvp.Value.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Server is shutting down",
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing WebSocket connection. ID: {ConnectionId}", kvp.Key);
            }
            finally
            {
                _connections.TryRemove(kvp.Key, out _);
            }
        });

        await Task.WhenAll(closeTasks);
        _logger.LogInformation("All WebSocket connections closed");
    }
}
