using System.ComponentModel.DataAnnotations;

namespace UCLEventScanner.Shared.DTOs;

public class CreateScannerDto
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
}

public class ScannerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class UpdateScannerDto
{
    [StringLength(100)]
    public string? Name { get; set; }
    
    public bool? IsActive { get; set; }
}
