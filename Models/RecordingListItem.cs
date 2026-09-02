namespace DawnCapture.Models;

public sealed class RecordingListItem
{
    public required string Name { get; init; }

    public required string DateDuration { get; init; }

    public required string TypeLabel { get; init; }

    public required string SizeLabel { get; init; }

    public bool IsAudio { get; init; }
}
