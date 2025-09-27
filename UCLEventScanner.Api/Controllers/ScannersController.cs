using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Api.Services;
using UCLEventScanner.Shared.DTOs;
using UCLEventScanner.Shared.Models;

namespace UCLEventScanner.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScannersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<ScannersController> _logger;
    private readonly IDynamicQueueManager _queueManager;

    public ScannersController(AppDbContext context, ILogger<ScannersController> logger, IDynamicQueueManager queueManager)
    {
        _context = context;
        _logger = logger;
        _queueManager = queueManager;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ScannerDto>>> GetScanners()
    {
        try
        {
            var scanners = await _context.Scanners
                .Select(s => new ScannerDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    IsActive = s.IsActive
                })
                .ToListAsync();

            return Ok(scanners);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving scanners");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<ScannerDto>>> GetActiveScanners()
    {
        try
        {
            var scanners = await _context.Scanners
                .Where(s => s.IsActive)
                .Select(s => new ScannerDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    IsActive = s.IsActive
                })
                .ToListAsync();

            return Ok(scanners);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active scanners");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ScannerDto>> GetScanner(int id)
    {
        try
        {
            var scanner = await _context.Scanners.FindAsync(id);
            if (scanner == null)
            {
                return NotFound();
            }

            var scannerDto = new ScannerDto
            {
                Id = scanner.Id,
                Name = scanner.Name,
                IsActive = scanner.IsActive
            };

            return Ok(scannerDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving scanner {ScannerId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost]
    public async Task<ActionResult<ScannerDto>> CreateScanner(CreateScannerDto createScannerDto)
    {
        try
        {
            var existingScanner = await _context.Scanners
                .FirstOrDefaultAsync(s => s.Name == createScannerDto.Name);

            if (existingScanner != null)
            {
                return BadRequest("Scanner with this name already exists");
            }

            var scanner = new Scanner
            {
                Name = createScannerDto.Name,
                IsActive = createScannerDto.IsActive
            };

            _context.Scanners.Add(scanner);
            await _context.SaveChangesAsync();

            if (scanner.IsActive)
            {
                await _queueManager.SetupQueuesForScanner(scanner.Id);
                _logger.LogInformation("Created queues for new scanner {ScannerId}: {ScannerName}", scanner.Id, scanner.Name);
            }

            var scannerDto = new ScannerDto
            {
                Id = scanner.Id,
                Name = scanner.Name,
                IsActive = scanner.IsActive
            };

            _logger.LogInformation("Created new scanner {ScannerId}: {ScannerName}", scanner.Id, scanner.Name);
            return CreatedAtAction(nameof(GetScanner), new { id = scanner.Id }, scannerDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating scanner");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateScanner(int id, UpdateScannerDto updateScannerDto)
    {
        try
        {
            var scanner = await _context.Scanners.FindAsync(id);
            if (scanner == null)
            {
                return NotFound();
            }

            var wasActive = scanner.IsActive;

            if (!string.IsNullOrEmpty(updateScannerDto.Name))
            {
                scanner.Name = updateScannerDto.Name;
            }

            if (updateScannerDto.IsActive.HasValue)
            {
                scanner.IsActive = updateScannerDto.IsActive.Value;
            }

            await _context.SaveChangesAsync();

            if (!wasActive && scanner.IsActive)
            {
                await _queueManager.SetupQueuesForScanner(scanner.Id);
                _logger.LogInformation("Scanner {ScannerId} activated - queues created", scanner.Id);
            }
            else if (wasActive && !scanner.IsActive)
            {
                try
                {
                    await _queueManager.DeleteQueuesForScanner(scanner.Id);
                    _logger.LogInformation("Scanner {ScannerId} deactivated - queues deleted", scanner.Id);
                }
                catch (Exception queueEx)
                {
                    _logger.LogWarning(queueEx, "Failed to delete queues for deactivated scanner {ScannerId}", scanner.Id);
                }
            }

            _logger.LogInformation("Updated scanner {ScannerId}: {ScannerName}, Active: {IsActive}", 
                scanner.Id, scanner.Name, scanner.IsActive);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating scanner {ScannerId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteScanner(int id)
    {
        try
        {
            var scanner = await _context.Scanners.FindAsync(id);
            if (scanner == null)
            {
                return NotFound();
            }

            var scannerName = scanner.Name;
            var wasActive = scanner.IsActive;

            _context.Scanners.Remove(scanner);
            await _context.SaveChangesAsync();

            if (wasActive)
            {
                try
                {
                    await _queueManager.DeleteQueuesForScanner(id);
                    _logger.LogInformation("Deleted queues for scanner {ScannerId}: {ScannerName}", id, scannerName);
                }
                catch (Exception queueEx)
                {
                    _logger.LogWarning(queueEx, "Failed to delete queues for scanner {ScannerId}, but scanner was deleted from database", id);
                }
            }

            _logger.LogInformation("Deleted scanner {ScannerId}: {ScannerName}", id, scannerName);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting scanner {ScannerId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}