using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.DTOs;

/// <summary>
/// DTO for scan requests (EIP Request-Reply Pattern)
/// </summary>
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

/// <summary>
/// DTO for scan responses
/// </summary>
public class ScanResponseDto
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ScannerId { get; set; }
    public string StudentId { get; set; } = string.Empty;
    public int EventId { get; set; }
}