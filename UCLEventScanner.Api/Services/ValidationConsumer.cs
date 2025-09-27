using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Shared.Messages;

namespace UCLEventScanner.Api.Services;

/// <summary>
/// EIP: ValidationConsumer - Message-driven consumer for validation requests
/// Consumes from dynamic queues (scan-requests-{ScannerId}), validates registration, sends reply
/// </summary>
public class ValidationConsumer : BackgroundService
{
    private readonly IRabbitMqConnectionService _connectionService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ValidationConsumer> _logger;
    private readonly string[] _queuePatterns = { "scan-requests-*" }; // EIP: Wildcards for consuming all scanner queues
    private IModel? _channel;
    private const string DIRECT_EXCHANGE = "scan-requests";

    public ValidationConsumer(IRabbitMqConnectionService connectionService,
                            IServiceScopeFactory scopeFactory,
                            ILogger<ValidationConsumer> logger)
    {
        _connectionService = connectionService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _channel = await _connectionService.CreateChannelAsync();
            
            // EIP: Set QoS for fair dispatch
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            // Setup consumer for all scanner queues
            await SetupQueueConsumers(stoppingToken);

            _logger.LogInformation("ValidationConsumer started");

            // Keep the service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                
                // Periodically check for new scanner queues
                await RefreshQueueConsumers(stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ValidationConsumer");
        }
    }

    private async Task SetupQueueConsumers(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Get all active scanners and setup consumers for their queues
            var activeScanners = await context.Scanners
                .Where(s => s.IsActive)
                .Select(s => s.Id)
                .ToListAsync(stoppingToken);

            foreach (var scannerId in activeScanners)
            {
                var queueName = $"scan-requests-{scannerId}";
                await SetupConsumerForQueue(queueName, scannerId);
            }

            _logger.LogInformation("Setup consumers for {ScannerCount} active scanners", activeScanners.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up queue consumers");
        }
    }

    private async Task SetupConsumerForQueue(string queueName, int scannerId)
    {
        try
        {
            if (_channel == null) return;

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                await HandleValidationRequest(scannerId, ea);
            };

            _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            _logger.LogDebug("Setup consumer for queue {QueueName}", queueName);
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to setup consumer for queue {QueueName}", queueName);
        }
    }

    private async Task HandleValidationRequest(int scannerId, BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var request = JsonConvert.DeserializeObject<ValidationRequestMessage>(message);

            if (request == null)
            {
                _logger.LogWarning("Failed to deserialize validation request");
                _channel?.BasicNack(ea.DeliveryTag, false, false);
                return;
            }

            _logger.LogInformation("Processing validation request - CorrelationId: {CorrelationId}, Student: {StudentId}", 
                request.CorrelationId, request.StudentId);

            // EIP: Validate registration in database
            var validationResult = await ValidateRegistrationAsync(request);

            // EIP: Send reply using DirectReplyTo
            await SendValidationReply(ea.BasicProperties, validationResult);

            // Acknowledge message
            _channel?.BasicAck(ea.DeliveryTag, false);

            _logger.LogInformation("Validation completed - CorrelationId: {CorrelationId}, Valid: {IsValid}", 
                request.CorrelationId, validationResult.IsValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling validation request");
            _channel?.BasicNack(ea.DeliveryTag, false, true); // Requeue on error
        }
    }

    private async Task<ValidationReplyMessage> ValidateRegistrationAsync(ValidationRequestMessage request)
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

    private async Task SendValidationReply(IBasicProperties requestProperties, ValidationReplyMessage reply)
    {
        if (_channel == null || string.IsNullOrEmpty(requestProperties.ReplyTo))
        {
            _logger.LogWarning("Cannot send reply - missing channel or ReplyTo property");
            return;
        }

        try
        {
            var replyProperties = _channel.CreateBasicProperties();
            replyProperties.CorrelationId = requestProperties.CorrelationId;

            var replyBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(reply));

            _channel.BasicPublish(exchange: "",
                                routingKey: requestProperties.ReplyTo,
                                basicProperties: replyProperties,
                                body: replyBody);

            _logger.LogDebug("Sent validation reply - CorrelationId: {CorrelationId}", reply.CorrelationId);
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending validation reply - CorrelationId: {CorrelationId}", reply.CorrelationId);
        }
    }

    private async Task RefreshQueueConsumers(CancellationToken stoppingToken)
    {
        // This could be enhanced to dynamically add consumers for new scanners
        // For now, we rely on application restart to pick up new scanners
        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        base.Dispose();
        _logger.LogInformation("ValidationConsumer disposed");
    }
}