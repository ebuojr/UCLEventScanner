using Microsoft.AspNetCore.SignalR.Client;
using UCLEventScanner.Shared.DTOs;

namespace UCLEventScanner.Client.Services;

public interface ISignalRService : IAsyncDisposable
{
    HubConnection? Connection { get; }
    HubConnectionState ConnectionState { get; }
    bool IsConnected { get; }
    
    Task<bool> ConnectAsync(string hubUrl);
    Task DisconnectAsync();
    
    void OnResultReceived(Func<int, bool, string, Task> handler);
    void OnConnectionStateChanged(Func<HubConnectionState, Task> handler);
}

public class SignalRService : ISignalRService, IAsyncDisposable
{
    private HubConnection? _hubConnection;

    public HubConnection? Connection => _hubConnection;
    public HubConnectionState ConnectionState => _hubConnection?.State ?? HubConnectionState.Disconnected;
    public bool IsConnected => ConnectionState == HubConnectionState.Connected;

    public async Task<bool> ConnectAsync(string hubUrl)
    {
        try
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                return true;
            }

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult((string?)null);
                    options.SkipNegotiation = false;
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets | 
                                        Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                })
                .WithAutomaticReconnect()
                .Build();

            await _hubConnection.StartAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    public void OnResultReceived(Func<int, bool, string, Task> handler)
    {
        _hubConnection?.On<int, bool, string>("ReceiveResult", handler);
    }

    public void OnConnectionStateChanged(Func<HubConnectionState, Task> handler)
    {
        if (_hubConnection != null)
        {
            _hubConnection.Closed += async (error) => await handler(HubConnectionState.Disconnected);
            _hubConnection.Reconnecting += async (error) => await handler(HubConnectionState.Reconnecting);
            _hubConnection.Reconnected += async (connectionId) => await handler(HubConnectionState.Connected);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}