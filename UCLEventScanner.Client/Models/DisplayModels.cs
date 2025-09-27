namespace UCLEventScanner.Client.Models;

/// <summary>
/// Model for display results shown on scanner displays
/// </summary>
public class DisplayResult
{
    public string StudentId { get; set; } = string.Empty;
    public int ScannerId { get; set; }
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// Model for scan history entries
/// </summary>
public class ScanHistoryEntry
{
    public string StudentId { get; set; } = string.Empty;
    public int ScannerId { get; set; }
    public string ScannerName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// Display configuration for different view types
/// </summary>
public static class DisplayConfig
{
    public const string ControllerView = "controller";
    public const string StudentView = "student";
    
    public static string GetDisplayTitle(string viewType, string scannerName)
    {
        return viewType.ToLower() switch
        {
            ControllerView => $"Scanner {scannerName}",
            StudentView => "Welcome",
            _ => "Scanner Display"
        };
    }
    
    public static string GetDisplaySubtitle(string viewType)
    {
        return viewType.ToLower() switch
        {
            ControllerView => "Controller Display",
            StudentView => "Please scan your ID",
            _ => "Scanner Display"
        };
    }
    
    public static string GetSuccessIcon(string viewType)
    {
        return viewType.ToLower() switch
        {
            ControllerView => "✓",
            StudentView => "🎉",
            _ => "✓"
        };
    }
    
    public static string GetErrorIcon(string viewType)
    {
        return viewType.ToLower() switch
        {
            ControllerView => "✗",
            StudentView => "😔",
            _ => "✗"
        };
    }
    
    public static string GetWaitingIcon(string viewType)
    {
        return viewType.ToLower() switch
        {
            ControllerView => "🔍",
            StudentView => "👋",
            _ => "🔍"
        };
    }
}