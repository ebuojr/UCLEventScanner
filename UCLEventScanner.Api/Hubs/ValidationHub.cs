using Microsoft.AspNetCore.SignalR;

namespace UCLEventScanner.Api.Hubs;

public class ValidationHub : Hub
{
    private readonly ILogger<ValidationHub> _logger;

    public ValidationHub(ILogger<ValidationHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinScannerGroup(int scannerId, string viewType)
    {
        var groupName = $"scanner-{scannerId}-{viewType.ToLower()}";
        
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation("Connection {ConnectionId} joined group {GroupName}", 
            Context.ConnectionId, groupName);

        await Clients.Caller.SendAsync("JoinedGroup", groupName);
    }

    public async Task LeaveScannerGroup(int scannerId, string viewType)
    {
        var groupName = $"scanner-{scannerId}-{viewType.ToLower()}";
        
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        
        _logger.LogInformation("Connection {ConnectionId} left group {GroupName}", 
            Context.ConnectionId, groupName);

        await Clients.Caller.SendAsync("LeftGroup", groupName);
    }

    public async Task GetActiveScanners()
    {
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