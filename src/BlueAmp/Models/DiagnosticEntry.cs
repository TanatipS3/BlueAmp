namespace BlueAmp.Models;

public enum DiagnosticLevel { Info, Success, Warning, Error }

public class DiagnosticEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public DiagnosticLevel Level { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
    public string[] Tags { get; init; } = [];
}
