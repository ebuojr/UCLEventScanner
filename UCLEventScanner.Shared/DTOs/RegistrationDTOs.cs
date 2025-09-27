using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.DTOs;

public class CreateRegistrationDto
{
    [Required]
    [StringLength(150)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [StringLength(50)]
    public string StudentId { get; set; } = string.Empty;
    
    [Required]
    public int EventId { get; set; }
}

public class RegistrationDto
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
}
