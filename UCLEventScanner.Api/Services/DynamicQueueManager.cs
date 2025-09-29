using RabbitMQ.Client;

namespace UCLEventScanner.Api.Services;

public interface IDynamicQueueManager
{
    Task SetupQueuesForScanner(int scannerId);
    Task DeleteQueuesForScanner(int scannerId);
    Task<string> GetScanRequestQueueName(int scannerId);
    Task InitializeExchanges();
}

public class DynamicQueueManager : IDynamicQueueManager
{
    private readonly IRabbitMqConnectionService _connectionService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DynamicQueueManager> _logger;

    private const string DIRECT_EXCHANGE = "scan-requests";
    private const string QUEUE_PREFIX = "scan-requests-";
    
    public DynamicQueueManager(IRabbitMqConnectionService connectionService, 
                              IServiceScopeFactory scopeFactory,
                              ILogger<DynamicQueueManager> logger)
    {
        _connectionService = connectionService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task InitializeExchanges()
    {
        try
        {
            using var channel = await _connectionService.CreateChannelAsync();
            
            channel.ExchangeDeclare(exchange: DIRECT_EXCHANGE, type: ExchangeType.Direct, durable: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize exchanges");
            throw;
        }
    }

    public async Task SetupQueuesForScanner(int scannerId)
    {
        try
        {
            using var channel = await _connectionService.CreateChannelAsync();

            await SetupQueuesForScannerInternal(channel, scannerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup queues for scanner {ScannerId}", scannerId);
            throw;
        }
    }

    public async Task DeleteQueuesForScanner(int scannerId)
    {
        try
        {
            using var channel = await _connectionService.CreateChannelAsync();
            var queueName = await GetScanRequestQueueName(scannerId);

            channel.QueueDelete(queue: queueName, ifUnused: false, ifEmpty: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete queues for scanner {ScannerId}", scannerId);
            throw;
        }
    }

    private async Task SetupQueuesForScannerInternal(IModel channel, int scannerId)
    {
        var queueName = await GetScanRequestQueueName(scannerId);
        var routingKey = $"scanner.{scannerId}";

        var queueArgs = new Dictionary<string, object>
        {
            {"x-message-ttl", 300000}, // 5 minutes TTL for messages
            {"x-expires", 1800000}     // 30 minutes TTL for queue if unused
        };

        channel.QueueDeclare(queue: queueName, 
                           durable: true, 
                           exclusive: false, 
                           autoDelete: false, 
                           arguments: queueArgs);

        channel.QueueBind(queue: queueName, 
                         exchange: DIRECT_EXCHANGE, 
                         routingKey: routingKey,
                         arguments: null);
        
        await Task.CompletedTask;
    }

    public Task<string> GetScanRequestQueueName(int scannerId)
    {
        return Task.FromResult($"{QUEUE_PREFIX}{scannerId}");
    }
}