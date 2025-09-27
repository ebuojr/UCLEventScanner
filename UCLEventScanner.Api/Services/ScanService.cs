using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Text;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Shared.DTOs;
using UCLEventScanner.Shared.Messages;

namespace UCLEventScanner.Api.Services;

/// <summary>
/// Service for handling scan requests using EIP Request-Reply pattern
/// </summary>
public interface IScanService
{
    Task<ScanResponseDto> ProcessScanAsync(ScanRequestDto scanRequest);
    Task<bool> CheckScannerHealthAsync(int scannerId);
}

public class ScanService : IScanService
{
    private readonly IRabbitMqConnectionService _connectionService;
    private readonly IDynamicQueueManager _queueManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanService> _logger;
    private readonly IResultBroadcaster _resultBroadcaster;
    
    // EIP: Correlation tracking for Request-Reply pattern
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ValidationReplyMessage>> _pendingRequests;
    private readonly Timer _timeoutTimer;
    private const int TIMEOUT_SECONDS = 30;
    private const string DIRECT_EXCHANGE = "scan-requests";

    public ScanService(IRabbitMqConnectionService connectionService,
                      IDynamicQueueManager queueManager,
                      IServiceScopeFactory scopeFactory,
                      ILogger<ScanService> logger,
                      IResultBroadcaster resultBroadcaster)
    {
        _connectionService = connectionService;
        _queueManager = queueManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _resultBroadcaster = resultBroadcaster;
        _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<ValidationReplyMessage>>();
        
        // Setup timer to clean up timed out requests
        _timeoutTimer = new Timer(CleanupTimedOutRequests, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// EIP Request-Reply Pattern: Send scan request and await response
    /// </summary>
    public async Task<ScanResponseDto> ProcessScanAsync(ScanRequestDto scanRequest)
    {
        // Validate scanner is active
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var scanner = await context.Scanners.FindAsync(scanRequest.ScannerId);
        if (scanner == null || !scanner.IsActive)
        {
            throw new InvalidOperationException($"Scanner {scanRequest.ScannerId} is not active");
        }

        // Generate correlation ID for Request-Reply pattern
        var correlationId = Guid.NewGuid().ToString();
        var routingKey = $"scanner.{scanRequest.ScannerId}";
        
        // Create request message
        var requestMessage = new ValidationRequestMessage
        {
            CorrelationId = correlationId,
            StudentId = scanRequest.StudentId,
            EventId = scanRequest.EventId,
            ScannerId = scanRequest.ScannerId
        };

        // Setup reply handling
        var tcs = new TaskCompletionSource<ValidationReplyMessage>();
        _pendingRequests[correlationId] = tcs;

        try
        {
            using var channel = await _connectionService.CreateChannelAsync();
            
            // EIP: DirectReplyTo for efficient replies - simplified implementation
            var replyQueueName = "amq.rabbitmq.reply-to";
            
            // Publish request message
            var properties = channel.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.ReplyTo = replyQueueName;
            properties.Expiration = (TIMEOUT_SECONDS * 1000).ToString(); // TTL in milliseconds

            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestMessage));
            
            channel.BasicPublish(exchange: DIRECT_EXCHANGE,
                               routingKey: routingKey,
                               basicProperties: properties,
                               body: messageBody);

            _logger.LogInformation("Sent scan request - CorrelationId: {CorrelationId}, Scanner: {ScannerId}", 
                correlationId, scanRequest.ScannerId);

            // For this simplified demo, we'll simulate the response after a short delay
            await Task.Delay(500); // Simulate processing time

            // Simulate validation response (in real implementation, this would come from the consumer)
            var simulatedReply = await SimulateValidationAsync(requestMessage);

            _logger.LogInformation("Received scan reply - CorrelationId: {CorrelationId}, Valid: {IsValid}", 
                correlationId, simulatedReply.IsValid);

            // EIP: Publish result to topic exchange for displays  
            await _resultBroadcaster.PublishValidationResultAsync(
                scanRequest.ScannerId, 
                simulatedReply.IsValid, 
                simulatedReply.Message,
                scanRequest.StudentId,
                scanRequest.EventId);

            return new ScanResponseDto
            {
                IsValid = simulatedReply.IsValid,
                Message = simulatedReply.Message,
                ScannerId = scanRequest.ScannerId,
                StudentId = scanRequest.StudentId,
                EventId = scanRequest.EventId
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Scan request timed out - CorrelationId: {CorrelationId}", correlationId);
            throw new TimeoutException("Scan request timed out");
        }
        finally
        {
            // Cleanup
            _pendingRequests.TryRemove(correlationId, out _);
        }
    }

    /// <summary>
    /// Check if scanner is healthy (queue exists and is accessible)
    /// </summary>
    public async Task<bool> CheckScannerHealthAsync(int scannerId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var scanner = await context.Scanners.FindAsync(scannerId);
            if (scanner == null || !scanner.IsActive)
            {
                return false;
            }

            // Try to access the scanner's queue
            using var channel = await _connectionService.CreateChannelAsync();
            var queueName = await _queueManager.GetScanRequestQueueName(scannerId);
            
            // This will throw if queue doesn't exist
            var queueInfo = channel.QueueDeclarePassive(queueName);
            return queueInfo != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scanner health check failed - Scanner: {ScannerId}", scannerId);
            return false;
        }
    }

    /// <summary>
    /// Simulate validation (in a real implementation, this would be handled by ValidationConsumer)
    /// </summary>
    private async Task<ValidationReplyMessage> SimulateValidationAsync(ValidationRequestMessage request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Check if student is registered for the event
            var isRegistered = await context.Registrations
                .AnyAsync(r => r.StudentId == request.StudentId && r.EventId == request.EventId);

            if (isRegistered)
            {
                return new ValidationReplyMessage
                {
                    CorrelationId = request.CorrelationId,
                    IsValid = true,
                    Message = "Welcome! Registration confirmed. 🎉"
                };
            }
            else
            {
                // Check if student exists
                var studentExists = await context.Students
                    .AnyAsync(s => s.Id == request.StudentId);

                var message = studentExists 
                    ? "Student not registered for this event. 😔"
                    : "Student ID not found. 😔";

                return new ValidationReplyMessage
                {
                    CorrelationId = request.CorrelationId,
                    IsValid = false,
                    Message = message
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating registration - Student: {StudentId}, Event: {EventId}", 
                request.StudentId, request.EventId);

            return new ValidationReplyMessage
            {
                CorrelationId = request.CorrelationId,
                IsValid = false,
                Message = "System error. Please try again. ⚠️"
            };
        }
    }

    private void CleanupTimedOutRequests(object? state)
    {
        var expiredKeys = new List<string>();
        var cutoff = DateTime.UtcNow.AddMinutes(-2); // Clean up requests older than 2 minutes

        foreach (var kvp in _pendingRequests)
        {
            if (kvp.Value.Task.IsCompleted || kvp.Value.Task.IsCanceled || kvp.Value.Task.IsFaulted)
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            if (_pendingRequests.TryRemove(key, out var tcs))
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetCanceled();
                }
            }
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired scan requests", expiredKeys.Count);
        }
    }
}