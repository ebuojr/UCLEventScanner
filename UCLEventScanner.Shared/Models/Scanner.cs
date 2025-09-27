using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.Models;

public class Scanner
{
    public int Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty; // e.g., "Line1", "Line2"
    
    public bool IsActive { get; set; }
}
