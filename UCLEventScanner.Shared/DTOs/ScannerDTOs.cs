using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.DTOs;

/// <summary>
/// DTO for creating/updating a scanner
/// </summary>
public class CreateScannerDto
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// DTO for scanner responses
/// </summary>
public class ScannerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>
/// DTO for updating scanner status
/// </summary>
public class UpdateScannerDto
{
    [StringLength(100)]
    public string? Name { get; set; }
    
    public bool? IsActive { get; set; }
}