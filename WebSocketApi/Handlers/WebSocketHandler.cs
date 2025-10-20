using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebSocketApi.Configuration;
using WebSocketApi.Services;

namespace WebSocketApi.Handlers;

public class WebSocketHandler
{
    private readonly ILogger<WebSocketHandler> _logger;
    private readonly WebSocketConnectionManager _connectionManager;
    private readonly WebSocketSettings _settings;
    private readonly DashboardDataService _dashboardDataService;

    public WebSocketHandler(
        ILogger<WebSocketHandler> logger,
        WebSocketConnectionManager connectionManager,
        IOptions<WebSocketSettings> settings,
        DashboardDataService dashboardDataService)
    {
        _logger = logger;
        _connectionManager = connectionManager;
        _settings = settings.Value;
        _dashboardDataService = dashboardDataService;
    }

    public async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            _logger.LogWarning("Non-WebSocket request received at WebSocket endpoint from {RemoteIp}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Expected a WebSocket request.");
            return;
        }

        WebSocket? socket = null;
        string? connectionId = null;

        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            connectionId = _connectionManager.AddConnection(socket);

            _logger.LogInformation("WebSocket connection established. ConnectionId: {ConnectionId}, RemoteIp: {RemoteIp}",
                connectionId, context.Connection.RemoteIpAddress);

            await ProcessMessagesAsync(socket, connectionId, context.RequestAborted);
        }
        catch (WebSocketException wsEx)
        {
            _logger.LogError(wsEx, "WebSocket error occurred. ConnectionId: {ConnectionId}, ErrorCode: {ErrorCode}",
                connectionId, wsEx.WebSocketErrorCode);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket operation cancelled. ConnectionId: {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling WebSocket connection. ConnectionId: {ConnectionId}",
                connectionId);
        }
        finally
        {
            await CleanupConnectionAsync(socket, connectionId);
        }
    }

    private async Task ProcessMessagesAsync(WebSocket socket, string connectionId, CancellationToken cancellationToken)
    {
        var buffer = new byte[_settings.ReceiveBufferSize];
        var messageBuffer = new List<byte>();
        var lastActivityTime = DateTime.UtcNow;

        using var timeoutCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                // Check for idle timeout
                var idleTime = DateTime.UtcNow - lastActivityTime;
                if (idleTime.TotalSeconds > _settings.IdleTimeoutSeconds)
                {
                    _logger.LogWarning("Connection idle timeout. ConnectionId: {ConnectionId}, IdleTime: {IdleTime}s",
                        connectionId, idleTime.TotalSeconds);
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection idle timeout",
                        CancellationToken.None);
                    break;
                }

                // Set receive timeout
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.ReceiveTimeoutSeconds));

                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), linkedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Receive timeout. ConnectionId: {ConnectionId}", connectionId);
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Receive timeout",
                        CancellationToken.None);
                    break;
                }

                lastActivityTime = DateTime.UtcNow;

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation(
                        "Close message received. ConnectionId: {ConnectionId}, Status: {CloseStatus}, Description: {CloseDescription}",
                        connectionId, result.CloseStatus, result.CloseStatusDescription);

                    await socket.CloseAsync(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription ?? "Client requested close",
                        CancellationToken.None);
                    break;
                }

                // Accumulate message fragments
                messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                // Check message size limit
                if (messageBuffer.Count > _settings.MaxMessageSize)
                {
                    _logger.LogWarning(
                        "Message size exceeded limit. ConnectionId: {ConnectionId}, Size: {Size}, Limit: {Limit}",
                        connectionId, messageBuffer.Count, _settings.MaxMessageSize);

                    await socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Message size exceeds maximum allowed",
                        CancellationToken.None);
                    break;
                }

                // Process complete message
                if (result.EndOfMessage)
                {
                    await ProcessCompleteMessageAsync(socket, connectionId, messageBuffer.ToArray(), result.MessageType, cancellationToken);
                    messageBuffer.Clear();
                }
            }
        }
        catch (WebSocketException wsEx) when (wsEx.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogWarning("Connection closed prematurely. ConnectionId: {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing messages. ConnectionId: {ConnectionId}", connectionId);
            throw;
        }
    }

    private async Task ProcessCompleteMessageAsync(
        WebSocket socket,
        string connectionId,
        byte[] messageData,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        try
        {
            if (messageType == WebSocketMessageType.Text)
            {
                var messageText = Encoding.UTF8.GetString(messageData);
                _logger.LogDebug("Received text message. ConnectionId: {ConnectionId}, Length: {Length}, Content: {Message}",
                    connectionId, messageData.Length, messageText);

                // Check if client is requesting dashboard data
                if (messageText.Trim().ToLower() == "get_dashboard_data")
                {
                    await SendDashboardDataAsync(socket, connectionId, cancellationToken);
                    return;
                }

                // Start streaming dashboard data periodically
                if (messageText.Trim().ToLower() == "start_stream")
                {
                    _logger.LogInformation("Starting dashboard data stream. ConnectionId: {ConnectionId}", connectionId);
                    await StreamDashboardDataAsync(socket, connectionId, cancellationToken);
                    return;
                }
            }

            // Echo back the message (default behavior)
            await SendMessageAsync(socket, connectionId, messageData, messageType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing complete message. ConnectionId: {ConnectionId}", connectionId);
            throw;
        }
    }

    private async Task SendDashboardDataAsync(WebSocket socket, string connectionId, CancellationToken cancellationToken)
    {
        try
        {
            var dashboardData = _dashboardDataService.GenerateMockData();
            var json = JsonSerializer.Serialize(dashboardData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var bytes = Encoding.UTF8.GetBytes(json);

            await SendMessageAsync(socket, connectionId, bytes, WebSocketMessageType.Text, cancellationToken);
            _logger.LogDebug("Dashboard data sent. ConnectionId: {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending dashboard data. ConnectionId: {ConnectionId}", connectionId);
            throw;
        }
    }

    private async Task StreamDashboardDataAsync(WebSocket socket, string connectionId, CancellationToken cancellationToken)
    {
        try
        {
            // Send dashboard data every 2 seconds
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                await SendDashboardDataAsync(socket, connectionId, cancellationToken);
                await Task.Delay(2000, cancellationToken); // Update every 2 seconds
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Dashboard data streaming cancelled. ConnectionId: {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming dashboard data. ConnectionId: {ConnectionId}", connectionId);
            throw;
        }
    }

    private async Task SendMessageAsync(
        WebSocket socket,
        string connectionId,
        byte[] message,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        try
        {
            if (socket.State != WebSocketState.Open)
            {
                _logger.LogWarning("Cannot send message, socket not open. ConnectionId: {ConnectionId}, State: {State}",
                    connectionId, socket.State);
                return;
            }

            await socket.SendAsync(
                new ArraySegment<byte>(message),
                messageType,
                endOfMessage: true,
                cancellationToken);

            _logger.LogDebug("Message sent successfully. ConnectionId: {ConnectionId}, Length: {Length}",
                connectionId, message.Length);
        }
        catch (WebSocketException wsEx)
        {
            _logger.LogError(wsEx, "WebSocket error sending message. ConnectionId: {ConnectionId}, ErrorCode: {ErrorCode}",
                connectionId, wsEx.WebSocketErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message. ConnectionId: {ConnectionId}", connectionId);
            throw;
        }
    }

    private async Task CleanupConnectionAsync(WebSocket? socket, string? connectionId)
    {
        try
        {
            if (connectionId != null)
            {
                _connectionManager.RemoveConnection(connectionId);
            }

            if (socket != null)
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Connection cleanup",
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error closing socket during cleanup. ConnectionId: {ConnectionId}",
                            connectionId);
                    }
                }

                socket.Dispose();
            }

            _logger.LogInformation("WebSocket connection cleaned up. ConnectionId: {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during connection cleanup. ConnectionId: {ConnectionId}", connectionId);
        }
    }
}
