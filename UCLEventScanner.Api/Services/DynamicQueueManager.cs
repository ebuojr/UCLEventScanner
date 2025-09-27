using RabbitMQ.Client;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UCLEventScanner.Api.Data;

namespace UCLEventScanner.Api.Services;

/// <summary>
/// Service for managing dynamic RabbitMQ queues based on active scanners
/// EIP: Dynamic Queue Management Pattern for scalability
/// </summary>
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

    // EIP: Exchange names and routing patterns
    private const string DIRECT_EXCHANGE = "scan-requests";
    private const string TOPIC_EXCHANGE = "validation-results";
    private const string QUEUE_PREFIX = "scan-requests-";
    
    public DynamicQueueManager(IRabbitMqConnectionService connectionService, 
                              IServiceScopeFactory scopeFactory,
                              ILogger<DynamicQueueManager> logger)
    {
        _connectionService = connectionService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Initialize exchanges only (called once on startup)
    /// </summary>
    public async Task InitializeExchanges()
    {
        try
        {
            using var channel = await _connectionService.CreateChannelAsync();
            
            // EIP: Declare Direct Exchange for Request-Reply pattern
            channel.ExchangeDeclare(exchange: DIRECT_EXCHANGE, type: ExchangeType.Direct, durable: true);
            _logger.LogInformation("Declared direct exchange: {Exchange}", DIRECT_EXCHANGE);

            // EIP: Declare Topic Exchange for Publish-Subscribe pattern
            channel.ExchangeDeclare(exchange: TOPIC_EXCHANGE, type: ExchangeType.Topic, durable: true);
            _logger.LogInformation("Declared topic exchange: {Exchange}", TOPIC_EXCHANGE);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ exchanges");
            throw;
        }
    }

    /// <summary>
    /// Setup queues for a specific scanner (when admin creates or activates a scanner)
    /// EIP: Dynamic infrastructure provisioning
    /// </summary>
    public async Task SetupQueuesForScanner(int scannerId)
    {
        try
        {
            using var channel = await _connectionService.CreateChannelAsync();
            
            // Ensure exchanges exist
            channel.ExchangeDeclare(exchange: DIRECT_EXCHANGE, type: ExchangeType.Direct, durable: true);
            channel.ExchangeDeclare(exchange: TOPIC_EXCHANGE, type: ExchangeType.Topic, durable: true);
            
            await SetupQueuesForScannerInternal(channel, scannerId);
            _logger.LogInformation("Setup queues for scanner {ScannerId}", scannerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup queues for scanner {ScannerId}", scannerId);
            throw;
        }
    }

    /// <summary>
    /// Delete queues for a specific scanner (when admin deletes a scanner)
    /// </summary>
    public async Task DeleteQueuesForScanner(int scannerId)
    {
        try
        {
            using var channel = await _connectionService.CreateChannelAsync();
            var queueName = await GetScanRequestQueueName(scannerId);

            // Delete the queue
            channel.QueueDelete(queue: queueName, ifUnused: false, ifEmpty: false);
            _logger.LogInformation("Deleted queue {QueueName} for scanner {ScannerId}", queueName, scannerId);
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

        // EIP: Declare queue with TTL for automatic cleanup of inactive scanners
        var queueArgs = new Dictionary<string, object>
        {
            {"x-message-ttl", 300000}, // 5 minutes TTL for messages
            {"x-expires", 1800000}     // 30 minutes TTL for queue if unused
        };

        // Declare queue for this scanner's requests
        channel.QueueDeclare(queue: queueName, 
                           durable: true, 
                           exclusive: false, 
                           autoDelete: false, 
                           arguments: queueArgs);

        // Bind queue to direct exchange for Request-Reply
        channel.QueueBind(queue: queueName, 
                         exchange: DIRECT_EXCHANGE, 
                         routingKey: routingKey,
                         arguments: null);

        _logger.LogDebug("Declared queue {QueueName} with routing key {RoutingKey}", queueName, routingKey);
        
        await Task.CompletedTask;
    }

    public Task<string> GetScanRequestQueueName(int scannerId)
    {
        return Task.FromResult($"{QUEUE_PREFIX}{scannerId}");
    }
}