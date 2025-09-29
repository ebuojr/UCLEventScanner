namespace UCLEventScanner.Shared.Messages;

public class ValidationRequestMessage
{
    public string CorrelationId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public int EventId { get; set; }
    public int ScannerId { get; set; }
}

public class ValidationReplyMessage
{
    public string CorrelationId { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
}
