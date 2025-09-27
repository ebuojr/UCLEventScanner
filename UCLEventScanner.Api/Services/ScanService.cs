using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Text;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Shared.DTOs;
using UCLEventScanner.Shared.Messages;

namespace UCLEventScanner.Api.Services;

public interface IScanService
{
    Task<ScanResponseDto> ProcessScanAsync(ScanRequestDto scanRequest);
}

public class ScanService : IScanService
{
    private readonly IRabbitMqConnectionService _connectionService;
    private readonly IDynamicQueueManager _queueManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanService> _logger;
    private readonly IResultBroadcaster _resultBroadcaster;
    
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
        
        _timeoutTimer = new Timer(CleanupTimedOutRequests, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task<ScanResponseDto> ProcessScanAsync(ScanRequestDto scanRequest)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var scanner = await context.Scanners.FindAsync(scanRequest.ScannerId);
        if (scanner == null || !scanner.IsActive)
        {
            throw new InvalidOperationException($"Scanner {scanRequest.ScannerId} is not active");
        }

        var correlationId = Guid.NewGuid().ToString();
        var routingKey = $"scanner.{scanRequest.ScannerId}";
        
        var requestMessage = new ValidationRequestMessage
        {
            CorrelationId = correlationId,
            StudentId = scanRequest.StudentId,
            EventId = scanRequest.EventId,
            ScannerId = scanRequest.ScannerId
        };

        var tcs = new TaskCompletionSource<ValidationReplyMessage>();
        _pendingRequests[correlationId] = tcs;

        try
        {
            using var channel = await _connectionService.CreateChannelAsync();
            
            var replyQueueName = "amq.rabbitmq.reply-to";
            
            var properties = channel.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.ReplyTo = replyQueueName;
            properties.Expiration = (TIMEOUT_SECONDS * 1000).ToString();

            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestMessage));
            
            channel.BasicPublish(exchange: DIRECT_EXCHANGE,
                               routingKey: routingKey,
                               basicProperties: properties,
                               body: messageBody);

            _logger.LogInformation("Sent scan request - CorrelationId: {CorrelationId}, Scanner: {ScannerId}", 
                correlationId, scanRequest.ScannerId);

            await Task.Delay(500);

            var simulatedReply = await SimulateValidationAsync(requestMessage);

            _logger.LogInformation("Received scan reply - CorrelationId: {CorrelationId}, Valid: {IsValid}", 
                correlationId, simulatedReply.IsValid);

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
            _pendingRequests.TryRemove(correlationId, out _);
        }
    }

    private async Task<ValidationReplyMessage> SimulateValidationAsync(ValidationRequestMessage request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
        var cutoff = DateTime.UtcNow.AddMinutes(-2);

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