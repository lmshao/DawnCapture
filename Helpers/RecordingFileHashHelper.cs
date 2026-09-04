using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DawnCapture.Helpers;

internal static class RecordingFileHashHelper
{
    public static async Task<string> ComputeSha256HexAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 1024 * 1024,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static bool IsQuickMatch(FileInfo fileInfo, long knownSize, DateTime knownLastWriteUtc)
    {
        return fileInfo.Length == knownSize
            && fileInfo.LastWriteTimeUtc.Ticks == knownLastWriteUtc.Ticks;
    }
}