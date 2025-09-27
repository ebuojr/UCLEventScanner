using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using UCLEventScanner.Api.Hubs;
using UCLEventScanner.Shared.Messages;

namespace UCLEventScanner.Api.Services;

public interface IResultBroadcaster
{
    Task PublishValidationResultAsync(int scannerId, bool isValid, string message, string studentId, int eventId);
}

public class ResultBroadcaster : BackgroundService, IResultBroadcaster
{
    private readonly IRabbitMqConnectionService _connectionService;
    private readonly IHubContext<ValidationHub> _hubContext;
    private readonly ILogger<ResultBroadcaster> _logger;
    private IModel? _channel;
    private const string TOPIC_EXCHANGE = "validation-results";
    private const string RESULT_QUEUE = "display-results";

    public ResultBroadcaster(IRabbitMqConnectionService connectionService,
                           IHubContext<ValidationHub> hubContext,
                           ILogger<ResultBroadcaster> logger)
    {
        _connectionService = connectionService;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _channel = await _connectionService.CreateChannelAsync();
            
            _channel.ExchangeDeclare(exchange: TOPIC_EXCHANGE, type: ExchangeType.Topic, durable: true);

            _channel.QueueDeclare(queue: RESULT_QUEUE, durable: true, exclusive: false, autoDelete: false);
            
            _channel.QueueBind(queue: RESULT_QUEUE, exchange: TOPIC_EXCHANGE, routingKey: "scanner.*.result");

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                await HandleResultMessage(ea);
            };

            _channel.BasicConsume(queue: RESULT_QUEUE, autoAck: true, consumer: consumer);

            _logger.LogInformation("ResultBroadcaster started - listening for validation results");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ResultBroadcaster");
        }
    }

    private async Task HandleResultMessage(BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var result = JsonConvert.DeserializeObject<ValidationResultMessage>(message);

            if (result == null)
            {
                _logger.LogWarning("Failed to deserialize validation result");
                return;
            }

            _logger.LogInformation("Broadcasting result - Scanner: {ScannerId}, Valid: {IsValid}", 
                result.ScannerId, result.IsValid);

            await BroadcastToDisplays(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling result message");
        }
    }

    private async Task BroadcastToDisplays(ValidationResultMessage result)
    {
        try
        {
            var controllerGroup = $"scanner-{result.ScannerId}-controller";
            await _hubContext.Clients.Group(controllerGroup).SendAsync("ReceiveResult", 
                result.ScannerId, result.IsValid, result.Message);

            var studentGroup = $"scanner-{result.ScannerId}-student";
            await _hubContext.Clients.Group(studentGroup).SendAsync("ReceiveResult", 
                result.ScannerId, result.IsValid, result.Message);

            _logger.LogDebug("Broadcasted result to SignalR groups for scanner {ScannerId}", result.ScannerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting to displays - Scanner: {ScannerId}", result.ScannerId);
        }
    }

    public async Task PublishValidationResultAsync(int scannerId, bool isValid, string message, 
        string studentId, int eventId)
    {
        try
        {
            if (_channel == null)
            {
                _logger.LogWarning("Cannot publish result - channel not available");
                return;
            }

            var result = new ValidationResultMessage
            {
                ScannerId = scannerId,
                IsValid = isValid,
                Message = message,
                StudentId = studentId,
                EventId = eventId,
                Timestamp = DateTime.UtcNow
            };

            var routingKey = $"scanner.{scannerId}.result";
            var messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));

            var properties = _channel.CreateBasicProperties();
            properties.Persistent = true;

            _channel.BasicPublish(exchange: TOPIC_EXCHANGE,
                                routingKey: routingKey,
                                basicProperties: properties,
                                body: messageBody);

            _logger.LogDebug("Published validation result - Scanner: {ScannerId}, RoutingKey: {RoutingKey}", 
                scannerId, routingKey);
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing validation result - Scanner: {ScannerId}", scannerId);
        }
    }

    public override void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        base.Dispose();
        _logger.LogInformation("ResultBroadcaster disposed");
    }
}