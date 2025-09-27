using Microsoft.AspNetCore.SignalR;

namespace UCLEventScanner.Api.Hubs;

/// <summary>
/// SignalR Hub for real-time validation result broadcasting
/// EIP: Publish-Subscribe pattern for display updates
/// Groups: "scanner-{id}-controller" and "scanner-{id}-student"
/// </summary>
public class ValidationHub : Hub
{
    private readonly ILogger<ValidationHub> _logger;

    public ValidationHub(ILogger<ValidationHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Join a scanner group for receiving validation results
    /// </summary>
    /// <param name="scannerId">The scanner ID</param>
    /// <param name="viewType">Either "controller" or "student"</param>
    public async Task JoinScannerGroup(int scannerId, string viewType)
    {
        var groupName = $"scanner-{scannerId}-{viewType.ToLower()}";
        
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation("Connection {ConnectionId} joined group {GroupName}", 
            Context.ConnectionId, groupName);

        // Send confirmation to the client
        await Clients.Caller.SendAsync("JoinedGroup", groupName);
    }

    /// <summary>
    /// Leave a scanner group
    /// </summary>
    /// <param name="scannerId">The scanner ID</param>
    /// <param name="viewType">Either "controller" or "student"</param>
    public async Task LeaveScannerGroup(int scannerId, string viewType)
    {
        var groupName = $"scanner-{scannerId}-{viewType.ToLower()}";
        
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation("Connection {ConnectionId} left group {GroupName}", 
            Context.ConnectionId, groupName);

        // Send confirmation to the client
        await Clients.Caller.SendAsync("LeftGroup", groupName);
    }

    /// <summary>
    /// Get all active scanner IDs (for client initialization)
    /// </summary>
    public async Task GetActiveScanners()
    {
        // This could query the database, but for simplicity, we'll let the client
        // make an HTTP request to the ScannersController
        await Clients.Caller.SendAsync("GetActiveScannersRequested");
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        
        if (exception != null)
        {
            _logger.LogWarning(exception, "Client disconnected with exception: {ConnectionId}", Context.ConnectionId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }
}