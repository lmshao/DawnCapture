using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;

namespace DawnCapture.Services;

public interface IRecordingLibraryService
{
    string GetOutputFolder();

    Task<IReadOnlyList<RecordingListItem>> LoadRecordingsAsync(CancellationToken cancellationToken = default);
}
