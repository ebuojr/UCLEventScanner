using Microsoft.AspNetCore.SignalR;
using UCLEventScanner.Api.Hubs;

namespace UCLEventScanner.Api.Services;

public interface IResultBroadcaster
{
    Task BroadcastResultAsync(int scannerId, bool isValid, string message);
}

public class ResultBroadcaster : IResultBroadcaster
{
    private readonly IHubContext<ValidationHub> _hubContext;
    private readonly ILogger<ResultBroadcaster> _logger;

    public ResultBroadcaster(IHubContext<ValidationHub> hubContext, ILogger<ResultBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task BroadcastResultAsync(int scannerId, bool isValid, string message)
    {
        try
        {
            _logger.LogInformation("Broadcasting result - Scanner: {ScannerId}, Valid: {IsValid}", 
                scannerId, isValid);

            var controllerGroup = $"scanner-{scannerId}-controller";
            await _hubContext.Clients.Group(controllerGroup).SendAsync("ReceiveResult", 
                scannerId, isValid, message);

            var studentGroup = $"scanner-{scannerId}-student";
            await _hubContext.Clients.Group(studentGroup).SendAsync("ReceiveResult", 
                scannerId, isValid, message);

            _logger.LogDebug("Broadcasted result to SignalR groups for scanner {ScannerId}", scannerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting to displays - Scanner: {ScannerId}", scannerId);
        }
    }
}