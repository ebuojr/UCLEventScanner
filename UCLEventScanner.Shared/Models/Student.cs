using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.Models;

/// <summary>
/// Entity representing a student in the system
/// </summary>
public class Student
{
    [Key]
    [Required]
    [StringLength(50)]
    public string Id { get; set; } = string.Empty; // StudentID (e.g., "STU001")
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [StringLength(150)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    // Navigation properties
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
}