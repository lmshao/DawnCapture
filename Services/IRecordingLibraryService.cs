// Copyright (c) 2026 SHAO Liming <lmshao@163.com>
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DawnCapture.Models;

namespace DawnCapture.Services;

public interface IRecordingLibraryService
{
    string GetOutputFolder();

    Task<IReadOnlyList<RecordingListItem>> LoadRecordingsAsync(CancellationToken cancellationToken = default);

    Task<bool> RenameRecordingAsync(
        Guid recordingId,
        string filePath,
        string newBaseName,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteRecordingAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
