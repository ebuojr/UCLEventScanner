using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Shared.DTOs;
using UCLEventScanner.Shared.Models;

namespace UCLEventScanner.Api.Controllers;

/// <summary>
/// Controller for managing events (CRUD operations)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<EventsController> _logger;

    public EventsController(AppDbContext context, ILogger<EventsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all events with registration counts
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EventDto>>> GetEvents()
    {
        try
        {
            var events = await _context.Events
                .Include(e => e.Registrations)
                .Select(e => new EventDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    Date = e.Date,
                    RegistrationCount = e.Registrations.Count
                })
                .ToListAsync();

            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving events");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get a specific event by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<EventDto>> GetEvent(int id)
    {
        try
        {
            var eventEntity = await _context.Events
                .Include(e => e.Registrations)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (eventEntity == null)
            {
                return NotFound();
            }

            var eventDto = new EventDto
            {
                Id = eventEntity.Id,
                Name = eventEntity.Name,
                Date = eventEntity.Date,
                RegistrationCount = eventEntity.Registrations.Count
            };

            return Ok(eventDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving event {EventId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Create a new event
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<EventDto>> CreateEvent(CreateEventDto createEventDto)
    {
        try
        {
            var eventEntity = new Event
            {
                Name = createEventDto.Name,
                Date = createEventDto.Date
            };

            _context.Events.Add(eventEntity);
            await _context.SaveChangesAsync();

            var eventDto = new EventDto
            {
                Id = eventEntity.Id,
                Name = eventEntity.Name,
                Date = eventEntity.Date,
                RegistrationCount = 0
            };

            _logger.LogInformation("Created new event: {EventName}", eventEntity.Name);
            return CreatedAtAction(nameof(GetEvent), new { id = eventEntity.Id }, eventDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Update an existing event
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateEvent(int id, CreateEventDto updateEventDto)
    {
        try
        {
            var eventEntity = await _context.Events.FindAsync(id);
            if (eventEntity == null)
            {
                return NotFound();
            }

            eventEntity.Name = updateEventDto.Name;
            eventEntity.Date = updateEventDto.Date;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated event {EventId}: {EventName}", id, eventEntity.Name);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event {EventId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Delete an event
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        try
        {
            var eventEntity = await _context.Events.FindAsync(id);
            if (eventEntity == null)
            {
                return NotFound();
            }

            _context.Events.Remove(eventEntity);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted event {EventId}: {EventName}", id, eventEntity.Name);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event {EventId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}