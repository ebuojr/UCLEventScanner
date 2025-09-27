using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Shared.DTOs;
using UCLEventScanner.Shared.Models;

namespace UCLEventScanner.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegistrationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<RegistrationsController> _logger;

    public RegistrationsController(AppDbContext context, ILogger<RegistrationsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RegistrationDto>>> GetRegistrations()
    {
        try
        {
            var registrations = await _context.Registrations
                .Include(r => r.Event)
                .Include(r => r.Student)
                .Select(r => new RegistrationDto
                {
                    Id = r.Id,
                    EventId = r.EventId,
                    EventName = r.Event.Name,
                    StudentId = r.StudentId,
                    StudentName = r.Student.Name,
                    RegisteredAt = r.RegisteredAt
                })
                .ToListAsync();

            return Ok(registrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving registrations");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("event/{eventId}")]
    public async Task<ActionResult<IEnumerable<RegistrationDto>>> GetRegistrationsByEvent(int eventId)
    {
        try
        {
            var registrations = await _context.Registrations
                .Where(r => r.EventId == eventId)
                .Include(r => r.Event)
                .Include(r => r.Student)
                .Select(r => new RegistrationDto
                {
                    Id = r.Id,
                    EventId = r.EventId,
                    EventName = r.Event.Name,
                    StudentId = r.StudentId,
                    StudentName = r.Student.Name,
                    RegisteredAt = r.RegisteredAt
                })
                .ToListAsync();

            return Ok(registrations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving registrations for event {EventId}", eventId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost]
    public async Task<ActionResult<RegistrationDto>> CreateRegistration(CreateRegistrationDto createRegistrationDto)
    {
        try
        {
            var eventEntity = await _context.Events.FindAsync(createRegistrationDto.EventId);
            if (eventEntity == null)
            {
                return BadRequest("Event not found");
            }

            var existingRegistration = await _context.Registrations
                .FirstOrDefaultAsync(r => r.EventId == createRegistrationDto.EventId && 
                                         r.StudentId == createRegistrationDto.StudentId);

            if (existingRegistration != null)
            {
                return BadRequest("Student is already registered for this event");
            }

            var student = await _context.Students.FindAsync(createRegistrationDto.StudentId);
            if (student == null)
            {
                student = new Student
                {
                    Id = createRegistrationDto.StudentId,
                    Name = createRegistrationDto.Name,
                    Email = createRegistrationDto.Email
                };
                _context.Students.Add(student);
            }
            else
            {
                student.Name = createRegistrationDto.Name;
                student.Email = createRegistrationDto.Email;
            }

            var registration = new Registration
            {
                EventId = createRegistrationDto.EventId,
                StudentId = createRegistrationDto.StudentId,
                RegisteredAt = DateTime.UtcNow
            };

            _context.Registrations.Add(registration);
            await _context.SaveChangesAsync();

            var registrationDto = new RegistrationDto
            {
                Id = registration.Id,
                EventId = registration.EventId,
                EventName = eventEntity.Name,
                StudentId = registration.StudentId,
                StudentName = student.Name,
                RegisteredAt = registration.RegisteredAt
            };

            _logger.LogInformation("Created registration for student {StudentId} to event {EventId}", 
                createRegistrationDto.StudentId, createRegistrationDto.EventId);

            return CreatedAtAction(nameof(GetRegistrations), registrationDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating registration");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("validate")]
    public async Task<ActionResult<bool>> ValidateRegistration([FromQuery] string studentId, [FromQuery] int eventId)
    {
        try
        {
            var isRegistered = await _context.Registrations
                .AnyAsync(r => r.StudentId == studentId && r.EventId == eventId);

            return Ok(isRegistered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating registration for student {StudentId} and event {EventId}", studentId, eventId);
            return StatusCode(500, "Internal server error");
        }
    }
}