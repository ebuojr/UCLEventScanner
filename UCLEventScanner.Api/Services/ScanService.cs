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

public interface IScanService
{
    Task<ScanResponseDto> ProcessScanAsync(ScanRequestDto scanRequest);
    Task<bool> CheckScannerHealthAsync(int scannerId);
}

public class ScanService : IScanService, IDisposable
{
    private readonly IRabbitMqConnectionService _connectionService;
    private readonly IDynamicQueueManager _queueManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IResultBroadcaster _resultBroadcaster;
    
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ValidationReplyMessage>> _pendingRequests;
    private readonly Timer _timeoutTimer;
    private IModel? _replyChannel;
    private string? _replyQueueName;
    private const int TIMEOUT_SECONDS = 30;
    private const string DIRECT_EXCHANGE = "scan-requests";

    public ScanService(IRabbitMqConnectionService connectionService,
                      IDynamicQueueManager queueManager,
                      IServiceScopeFactory scopeFactory,
                      IResultBroadcaster resultBroadcaster)
    {
        _connectionService = connectionService;
        _queueManager = queueManager;
        _scopeFactory = scopeFactory;
        _resultBroadcaster = resultBroadcaster;
        _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<ValidationReplyMessage>>();
        
        _timeoutTimer = new Timer(CleanupTimedOutRequests, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        
        InitializeReplyConsumer();
    }

    private async void InitializeReplyConsumer()
    {
        try
        {
            _replyChannel = await _connectionService.CreateChannelAsync();
            _replyQueueName = _replyChannel.QueueDeclare().QueueName;

            var consumer = new EventingBasicConsumer(_replyChannel);
            consumer.Received += HandleValidationReply;

            _replyChannel.BasicConsume(queue: _replyQueueName, autoAck: true, consumer: consumer);
        }
        catch (Exception)
        {
        }
    }

    private async void HandleValidationReply(object? model, BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var reply = JsonConvert.DeserializeObject<ValidationReplyMessage>(message);

            if (reply != null && _pendingRequests.TryRemove(reply.CorrelationId, out var tcs))
            {
                tcs.SetResult(reply);
            }
        }
        catch (Exception)
        {
        }
        
        await Task.CompletedTask;
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

        if (_replyQueueName == null)
        {
            throw new InvalidOperationException("Reply consumer not initialized");
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
            
            var properties = channel.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.ReplyTo = _replyQueueName;
            properties.Expiration = (TIMEOUT_SECONDS * 1000).ToString();

            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestMessage));
            
            channel.BasicPublish(exchange: DIRECT_EXCHANGE,
                               routingKey: routingKey,
                               basicProperties: properties,
                               body: messageBody);

            var reply = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(TIMEOUT_SECONDS));

            await _resultBroadcaster.BroadcastResultAsync(
                scanRequest.ScannerId, 
                reply.IsValid, 
                reply.Message,
                reply.StudentId,
                reply.StudentName);

            return new ScanResponseDto
            {
                IsValid = reply.IsValid,
                Message = reply.Message,
                ScannerId = scanRequest.ScannerId,
                StudentId = scanRequest.StudentId,
                EventId = scanRequest.EventId
            };
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Scan request timed out");
        }
        //finally
        //{
        //    _pendingRequests.TryRemove(correlationId, out _);
        //}
    }

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

            using var channel = await _connectionService.CreateChannelAsync();
            var queueName = await _queueManager.GetScanRequestQueueName(scannerId);
            
            var queueInfo = channel.QueueDeclarePassive(queueName);
            return queueInfo != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void CleanupTimedOutRequests(object? state)
    {
        var expiredKeys = new List<string>();

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
        }
    }

    public void Dispose()
    {
        _timeoutTimer?.Dispose();
        _replyChannel?.Close();
        _replyChannel?.Dispose();
        
        foreach (var kvp in _pendingRequests)
        {
            if (!kvp.Value.Task.IsCompleted)
            {
                kvp.Value.SetCanceled();
            }
        }
        _pendingRequests.Clear();
    }
}