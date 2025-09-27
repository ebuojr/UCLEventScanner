using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.DTOs;

/// <summary>
/// DTO for creating a new event
/// </summary>
public class CreateEventDto
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    public DateTime Date { get; set; }
}

/// <summary>
/// DTO for event responses
/// </summary>
public class EventDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int RegistrationCount { get; set; }
}