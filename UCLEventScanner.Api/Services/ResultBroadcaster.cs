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

    public ResultBroadcaster(IHubContext<ValidationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task BroadcastResultAsync(int scannerId, bool isValid, string message)
    {
        try
        {
            var controllerGroup = $"scanner-{scannerId}-controller";
            await _hubContext.Clients.Group(controllerGroup).SendAsync("ReceiveResult", 
                scannerId, isValid, message);

            var studentGroup = $"scanner-{scannerId}-student";
            await _hubContext.Clients.Group(studentGroup).SendAsync("ReceiveResult", 
                scannerId, isValid, message);
        }
        catch (Exception)
        {
        }
    }
}