using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.Models;

/// <summary>
/// Entity representing an event in the system
/// </summary>
public class Event
{
    public int Id { get; set; }
    
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
    
    public DateTime Date { get; set; }
    
    // Navigation properties
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
}