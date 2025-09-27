using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Api.Services;
using UCLEventScanner.Shared.DTOs;
using UCLEventScanner.Shared.Models;

namespace UCLEventScanner.Api.Controllers;

/// <summary>
/// Controller for managing scanners (CRUD for dynamic scanner lines)
/// </summary>
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

    /// <summary>
    /// Get all scanners
    /// </summary>
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

    /// <summary>
    /// Get active scanners only
    /// </summary>
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

    /// <summary>
    /// Get a specific scanner by ID
    /// </summary>
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

    /// <summary>
    /// Create a new scanner (e.g., "Open Line 4")
    /// EIP: Dynamically creates new queues for scalability
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ScannerDto>> CreateScanner(CreateScannerDto createScannerDto)
    {
        try
        {
            // Check if scanner with same name already exists
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

            // EIP: Dynamic Queue Management - Setup queues for new scanner if active
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

    /// <summary>
    /// Update a scanner (e.g., toggle active status)
    /// EIP: Dynamic queue management based on scanner status
    /// </summary>
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

            // Update scanner properties
            if (!string.IsNullOrEmpty(updateScannerDto.Name))
            {
                scanner.Name = updateScannerDto.Name;
            }

            if (updateScannerDto.IsActive.HasValue)
            {
                scanner.IsActive = updateScannerDto.IsActive.Value;
            }

            await _context.SaveChangesAsync();

            // EIP: Dynamic Queue Management - Handle queue setup/cleanup based on status change
            if (!wasActive && scanner.IsActive)
            {
                // Scanner became active - setup queues
                await _queueManager.SetupQueuesForScanner(scanner.Id);
                _logger.LogInformation("Scanner {ScannerId} activated - queues created", scanner.Id);
            }
            else if (wasActive && !scanner.IsActive)
            {
                // Scanner became inactive - cleanup can be handled by queue TTL
                _logger.LogInformation("Scanner {ScannerId} deactivated - queues will expire", scanner.Id);
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

    /// <summary>
    /// Delete a scanner
    /// </summary>
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

            _context.Scanners.Remove(scanner);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted scanner {ScannerId}: {ScannerName}", id, scanner.Name);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting scanner {ScannerId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}