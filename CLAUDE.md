# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an ASP.NET Core 8.0 minimal API application that provides real-time WebSocket functionality for streaming dashboard data. The application sends JSON-formatted data (metrics and table data) to connected clients for real-time visualization. The project is configured for containerization with Docker and includes health check endpoints for load balancer integration.

## Build and Run Commands

**Build the project:**
```bash
dotnet build WebSocketApi/WebSocketApi.csproj
```

**Run the application:**
```bash
dotnet run --project WebSocketApi/WebSocketApi.csproj
```
- The app runs on http://localhost:5015 (HTTP) and https://localhost:7217 (HTTPS) by default
- WebSocket endpoint available at `/ws`
- Health check endpoint at `/health`

**Build and run with Docker:**
```bash
docker build -f WebSocketApi/Dockerfile -t websocketapi .
docker run -p 8080:8080 websocketapi
```

**Restore dependencies:**
```bash
dotnet restore WebSocketApi/WebSocketApi.csproj
```

## Architecture

### Core Components

**Program.cs** - Application entry point and service configuration
- Minimal API approach with dependency injection
- CORS policy configured from `appsettings.json`
- WebSocket middleware with configurable keep-alive interval
- Graceful shutdown handling that closes all active WebSocket connections

**WebSocketHandler** (`Handlers/WebSocketHandler.cs`)
- Comprehensive error handling with try-catch blocks around all WebSocket operations
- Message lifecycle management (receive, process, send)
- Connection timeout handling (idle timeout and receive timeout)
- Message size validation (enforces maximum message size limit)
- Fragment accumulation for large messages spanning multiple frames
- Dashboard data streaming with two modes:
  - `start_stream` - Auto-pushes JSON data every 2 seconds
  - `get_dashboard_data` - Single request/response for dashboard data
- JSON serialization with camelCase naming for frontend consumption
- Structured logging for all connection events and errors

**WebSocketConnectionManager** (`Services/WebSocketConnectionManager.cs`)
- Thread-safe connection tracking using `ConcurrentDictionary`
- Connection lifecycle management (add, remove, cleanup)
- Graceful shutdown support - closes all connections when application stops
- Connection counting and enumeration for monitoring

**DashboardDataService** (`Services/DashboardDataService.cs`)
- Generates mock dashboard data with random values
- Produces metrics (CPU, Memory, Disk, Network, Active Connections)
- Produces table data (10 task rows with status, values, timestamps)
- Data refreshes with each request for real-time simulation

### Configuration

**WebSocketSettings** (`Configuration/WebSocketSettings.cs`)
- `KeepAliveIntervalSeconds` - Keep-alive ping interval (default: 30s)
- `ReceiveBufferSize` - Buffer size for receiving message fragments (default: 16KB)
- `MaxMessageSize` - Maximum total message size (default: 1MB)
- `ReceiveTimeoutSeconds` - Timeout for receive operations (default: 120s)
- `IdleTimeoutSeconds` - Timeout for idle connections (default: 300s)

**CorsSettings** (`Configuration/CorsSettings.cs`)
- `AllowedOrigins` - Array of permitted CORS origins
- Configured per environment in appsettings files

### Error Handling & Resilience

- **WebSocketException handling** - Catches and logs WebSocket-specific errors with error codes
- **Timeout management** - Both receive timeouts and idle connection timeouts
- **Connection cleanup** - Guaranteed cleanup in finally blocks
- **Graceful degradation** - Handles premature disconnections, message size violations
- **Structured logging** - All errors logged with connection context for debugging

### Message Flow

1. Client connects → WebSocket upgrade accepted → Connection registered in manager
2. Messages received in fragments → Accumulated until `EndOfMessage` flag
3. Complete message validated against size limits
4. Message processed based on command:
   - `start_stream` → Begins continuous data streaming (every 2 seconds)
   - `get_dashboard_data` → Sends single dashboard data snapshot
   - Other messages → Echoed back to client
5. On close/error → Connection cleaned up and removed from manager

### Dashboard Data Structure

The WebSocket sends JSON data in the following format:

```json
{
  "timestamp": "2025-10-20T12:34:56.789Z",
  "metrics": [
    {
      "name": "CPU Usage",
      "value": 75.5,
      "maxValue": 100,
      "unit": "%"
    }
  ],
  "tableData": [
    {
      "id": 1,
      "name": "Task Alpha",
      "status": "Active",
      "value": 123.45,
      "lastUpdated": "2025-10-20T12:34:56.789Z"
    }
  ]
}
```

**Metrics** include: CPU Usage, Memory Usage, Disk Space, Network Traffic, Active Connections
**Table Data** includes: 10 sample tasks with random statuses (Active, Pending, Completed, Error)

**Dependencies:**
- `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` (1.22.1) - Docker support
- `Swashbuckle.AspNetCore` (6.6.2) - API documentation (though WebSocket endpoint won't appear in Swagger)

## Configuration Management

**appsettings.json** - Production configuration
- Base WebSocket settings (timeouts, buffer sizes, message limits)
- Production CORS origins
- Logging configuration

**appsettings.Development.json** - Development overrides
- Debug-level logging for troubleshooting
- Local development CORS origins (localhost:3000, localhost:3001)

**Environment-specific settings:**
```bash
# To use different configuration per environment
export ASPNETCORE_ENVIRONMENT=Production
dotnet run --project WebSocketApi/WebSocketApi.csproj
```

## Development Notes

**User Secrets:** The project uses user secrets (ID: `39bdb70f-7cab-49f2-a5fb-7b32e56a8c12`) for sensitive configuration. Access via:
```bash
dotnet user-secrets set "KeyName" "Value" --project WebSocketApi/WebSocketApi.csproj
```

**Target Framework:** .NET 8.0 with nullable reference types and implicit usings enabled

**Docker:** Multi-stage Dockerfile optimized for Visual Studio fast debug mode and production builds. Exposes ports 8080 (HTTP) and 8081 (HTTPS).

**Logging Levels:**
- Production: Information level for WebSocketApi namespace
- Development: Debug level for detailed troubleshooting
- All WebSocket connection events, errors, and lifecycle changes are logged with structured context

## Testing the WebSocket Dashboard

**Using the Next.js Frontend:**
1. Ensure the API is running in Visual Studio (F5)
2. Navigate to the Next.js dashboard page
3. Click "Connect" to establish WebSocket connection
4. Send `start_stream` message to begin receiving real-time data updates
5. Dashboard will update every 2 seconds with new mock data

**Using Browser Console:**
```javascript
const ws = new WebSocket('ws://localhost:5015/ws');
ws.onopen = () => ws.send('start_stream');
ws.onmessage = (event) => console.log(JSON.parse(event.data));
```

**WebSocket Commands:**
- `start_stream` - Start continuous dashboard data streaming (2-second interval)
- `get_dashboard_data` - Get a single snapshot of dashboard data
- Any other text - Will be echoed back (legacy behavior)
