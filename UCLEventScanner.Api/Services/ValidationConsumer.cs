using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Shared.Messages;

namespace UCLEventScanner.Api.Services;

public class ValidationConsumer : BackgroundService
{
    private readonly IRabbitMqConnectionService _connectionService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ValidationConsumer> _logger;
    private readonly string[] _queuePatterns = { "scan-requests-*" };
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
            
            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            await SetupQueueConsumers(stoppingToken);

            _logger.LogInformation("ValidationConsumer started");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                
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
            
            var activeScanners = await context.Scanners
                .Where(s => s.IsActive)
                .ToListAsync(stoppingToken);

            foreach (var scanner in activeScanners)
            {
                SetupConsumerForScanner(scanner.Id);
            }

            _logger.LogInformation("Setup consumers for {Count} active scanners", activeScanners.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up queue consumers");
        }
    }

    private void SetupConsumerForScanner(int scannerId)
    {
        try
        {
            if (_channel == null) return;

            var queueName = $"scan-requests-{scannerId}";
            
            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                await HandleValidationRequest(ea);
            };

            _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
            
            _logger.LogDebug("Setup consumer for scanner {ScannerId} queue: {QueueName}", scannerId, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to setup consumer for scanner {ScannerId}", scannerId);
        }
    }

    private async Task HandleValidationRequest(BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var request = JsonConvert.DeserializeObject<ValidationRequestMessage>(message);

            if (request == null)
            {
                _logger.LogWarning("Invalid validation request message");
                _channel?.BasicNack(ea.DeliveryTag, false, false);
                return;
            }

            _logger.LogDebug("Processing validation request - CorrelationId: {CorrelationId}, Student: {StudentId}", 
                request.CorrelationId, request.StudentId);

            var reply = await ValidateRegistrationAsync(request);

            await SendReplyAsync(ea, reply);

            _channel?.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing validation request");
            _channel?.BasicNack(ea.DeliveryTag, false, true);
        }
    }

    private async Task<ValidationReplyMessage> ValidateRegistrationAsync(ValidationRequestMessage request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var student = await context.Students
                .FirstOrDefaultAsync(s => s.Id == request.StudentId);

            var isRegistered = await context.Registrations
                .AnyAsync(r => r.StudentId == request.StudentId && r.EventId == request.EventId);

            if (isRegistered)
            {
                return new ValidationReplyMessage
                {
                    CorrelationId = request.CorrelationId,
                    IsValid = true,
                    Message = "Welcome! Registration confirmed. 🎉",
                    StudentId = request.StudentId,
                    StudentName = student?.Name ?? "Unknown Student"
                };
            }
            else
            {
                var message = student != null 
                    ? "Student not registered for this event. 😔"
                    : "Student ID not found. 😔";

                return new ValidationReplyMessage
                {
                    CorrelationId = request.CorrelationId,
                    IsValid = false,
                    Message = message,
                    StudentId = request.StudentId,
                    StudentName = student?.Name ?? "Unknown Student"
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
                Message = "System error. Please try again. ⚠️",
                StudentId = request.StudentId,
                StudentName = "Unknown Student"
            };
        }
    }

    private async Task SendReplyAsync(BasicDeliverEventArgs ea, ValidationReplyMessage reply)
    {
        try
        {
            if (_channel == null || string.IsNullOrEmpty(ea.BasicProperties.ReplyTo)) return;

            var replyProperties = _channel.CreateBasicProperties();
            replyProperties.CorrelationId = ea.BasicProperties.CorrelationId;

            var replyBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(reply));

            _channel.BasicPublish(exchange: string.Empty,
                                routingKey: ea.BasicProperties.ReplyTo,
                                basicProperties: replyProperties,
                                body: replyBody);

            _logger.LogDebug("Sent validation reply - CorrelationId: {CorrelationId}, Valid: {IsValid}", 
                reply.CorrelationId, reply.IsValid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending validation reply - CorrelationId: {CorrelationId}", 
                reply.CorrelationId);
        }

        await Task.CompletedTask;
    }

    private async Task RefreshQueueConsumers(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var activeScanners = await context.Scanners
                .Where(s => s.IsActive)
                .Select(s => s.Id)
                .ToListAsync(stoppingToken);

            foreach (var scannerId in activeScanners)
            {
                var queueName = $"scan-requests-{scannerId}";
                try
                {
                    _channel?.QueueDeclarePassive(queueName);
                }
                catch
                {
                    SetupConsumerForScanner(scannerId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error refreshing queue consumers");
        }
    }

    public override void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        base.Dispose();
        _logger.LogInformation("ValidationConsumer disposed");
    }
}