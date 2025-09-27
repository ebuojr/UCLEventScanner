using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UCLEventScanner.Shared.Models;

public class Registration
{
    public int Id { get; set; }
    
    public int EventId { get; set; }
    
    [Required]
    [StringLength(50)]
    public string StudentId { get; set; } = string.Empty;
    
    public DateTime RegisteredAt { get; set; }
    
    [ForeignKey(nameof(EventId))]
    public Event Event { get; set; } = null!;
    
    [ForeignKey(nameof(StudentId))]
    public Student Student { get; set; } = null!;
}
