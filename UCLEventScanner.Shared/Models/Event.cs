using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.Models;

public class Event
{
    public int Id { get; set; }
    
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
    
    public DateTime Date { get; set; }
    
    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
}
