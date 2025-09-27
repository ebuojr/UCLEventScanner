using RabbitMQ.Client;

namespace UCLEventScanner.Api.Services;

public interface IRabbitMqConnectionService
{
    IConnection GetConnection();
    Task<IModel> CreateChannelAsync();
}

public class RabbitMqConnectionService : IRabbitMqConnectionService, IDisposable
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMqConnectionService> _logger;
    private bool _disposed = false;

    public RabbitMqConnectionService(ILogger<RabbitMqConnectionService> logger)
    {
        _logger = logger;
        
        var factory = new ConnectionFactory()
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        try
        {
            _connection = factory.CreateConnection("UCLEventScanner");
            _logger.LogInformation("RabbitMQ connection established");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish RabbitMQ connection");
            throw;
        }
    }

    public IConnection GetConnection()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RabbitMqConnectionService));
        }

        if (_connection == null || !_connection.IsOpen)
        {
            throw new InvalidOperationException("RabbitMQ connection is not available");
        }

        return _connection;
    }

    public async Task<IModel> CreateChannelAsync()
    {
        return await Task.FromResult(GetConnection().CreateModel());
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
            _logger.LogInformation("RabbitMQ connection disposed");
        }
    }
}