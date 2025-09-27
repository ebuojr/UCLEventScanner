namespace UCLEventScanner.Shared.Messages;

/// <summary>
/// EIP Request-Reply Pattern: Request message for validation
/// Sent to scan-requests-{ScannerId} queue
/// </summary>
public class ValidationRequestMessage
{
    public string CorrelationId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public int EventId { get; set; }
    public int ScannerId { get; set; }
}

/// <summary>
/// EIP Request-Reply Pattern: Reply message for validation
/// Sent back using DirectReplyTo
/// </summary>
public class ValidationReplyMessage
{
    public string CorrelationId { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// EIP Publish-Subscribe Pattern: Result message for displays
/// Published to topic exchange with routing key: scanner.{ScannerId}.result
/// </summary>
public class ValidationResultMessage
{
    public int ScannerId { get; set; }
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public int EventId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}