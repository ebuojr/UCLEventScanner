using Microsoft.AspNetCore.Mvc;
using UCLEventScanner.Api.Services;
using UCLEventScanner.Shared.DTOs;

namespace UCLEventScanner.Api.Controllers;

/// <summary>
/// Controller for handling scan operations
/// EIP: Request-Reply Pattern implementation
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ScanController : ControllerBase
{
    private readonly IScanService _scanService;
    private readonly ILogger<ScanController> _logger;

    public ScanController(IScanService scanService, ILogger<ScanController> logger)
    {
        _scanService = scanService;
        _logger = logger;
    }

    /// <summary>
    /// Main scan endpoint - Implements EIP Request-Reply Pattern
    /// POST /api/scan { StudentId, EventId, ScannerId }
    /// Publishes RequestMessage to RabbitMQ and awaits reply using CorrelationId
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ScanResponseDto>> Scan([FromBody] ScanRequestDto scanRequest)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _logger.LogInformation("Scan request received - Student: {StudentId}, Event: {EventId}, Scanner: {ScannerId}", 
                scanRequest.StudentId, scanRequest.EventId, scanRequest.ScannerId);

            // EIP Request-Reply: Send request and await reply
            var scanResponse = await _scanService.ProcessScanAsync(scanRequest);

            _logger.LogInformation("Scan processed - Student: {StudentId}, Valid: {IsValid}, Message: {Message}", 
                scanRequest.StudentId, scanResponse.IsValid, scanResponse.Message);

            return Ok(scanResponse);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Scan request timeout - Student: {StudentId}, Scanner: {ScannerId}", 
                scanRequest.StudentId, scanRequest.ScannerId);
            
            return StatusCode(408, new ScanResponseDto 
            { 
                IsValid = false, 
                Message = "Scan request timeout. Please try again.",
                ScannerId = scanRequest.ScannerId,
                StudentId = scanRequest.StudentId,
                EventId = scanRequest.EventId
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Scanner not active - Scanner: {ScannerId}", scanRequest.ScannerId);
            
            return BadRequest(new ScanResponseDto 
            { 
                IsValid = false, 
                Message = "Scanner is not active.",
                ScannerId = scanRequest.ScannerId,
                StudentId = scanRequest.StudentId,
                EventId = scanRequest.EventId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing scan - Student: {StudentId}, Scanner: {ScannerId}", 
                scanRequest.StudentId, scanRequest.ScannerId);

            return StatusCode(500, new ScanResponseDto 
            { 
                IsValid = false, 
                Message = "Internal server error. Please try again.",
                ScannerId = scanRequest.ScannerId,
                StudentId = scanRequest.StudentId,
                EventId = scanRequest.EventId
            });
        }
    }

    /// <summary>
    /// Health check endpoint for scanners
    /// </summary>
    [HttpGet("health/{scannerId}")]
    public async Task<ActionResult> CheckScannerHealth(int scannerId)
    {
        try
        {
            var isHealthy = await _scanService.CheckScannerHealthAsync(scannerId);
            
            if (isHealthy)
            {
                return Ok(new { ScannerId = scannerId, Status = "Healthy", Timestamp = DateTime.UtcNow });
            }
            else
            {
                return ServiceUnavailable(new { ScannerId = scannerId, Status = "Unhealthy", Timestamp = DateTime.UtcNow });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking scanner health - Scanner: {ScannerId}", scannerId);
            return StatusCode(500, new { ScannerId = scannerId, Status = "Error", Timestamp = DateTime.UtcNow });
        }
    }

    private ObjectResult ServiceUnavailable(object value)
    {
        return StatusCode(503, value);
    }
}