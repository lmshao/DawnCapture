namespace DawnCapture.Models;

public readonly struct OutputFolderChangeResult
{
    public bool IsSuccess { get; init; }

    public bool IsUnchanged { get; init; }

    public string? ErrorMessage { get; init; }

    public static OutputFolderChangeResult Success() => new() { IsSuccess = true };

    public static OutputFolderChangeResult Unchanged() => new() { IsSuccess = true, IsUnchanged = true };

    public static OutputFolderChangeResult Failed(string message) => new() { ErrorMessage = message };
}
