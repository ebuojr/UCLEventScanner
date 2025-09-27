using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.DTOs;

public class ScanRequestDto
{
    [Required]
    [StringLength(50)]
    public string StudentId { get; set; } = string.Empty;
    
    [Required]
    public int EventId { get; set; }
    
    [Required]
    public int ScannerId { get; set; }
}

public class ScanResponseDto
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ScannerId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public int EventId { get; set; }
}
