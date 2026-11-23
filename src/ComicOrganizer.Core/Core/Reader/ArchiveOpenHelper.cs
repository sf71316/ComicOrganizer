using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 壓縮檔容錯開啟輔助：
/// 1. 優先走 SharpCompress 原生 <see cref="ArchiveFactory.OpenArchive(string, ReaderOptions?)"/> 快速路徑；
/// 2. 部分歷史下載壓縮檔會在檔頭前方多出 1 byte 換行（0x0A）、UTF-8 BOM 或自解壓前置 stub，
///    導致 SharpCompress 從 offset 0 檢查魔術字元時失敗（"Cannot determine compressed stream type"）。
///    此時會自動透過 ZIP EOCD 偏移差值與前段魔術簽章掃描（ZIP / RAR / 7z）定位實際起點，
///    以零複製的 <see cref="OffsetReadOnlyStream"/> 重新開啟並確保於歸檔釋放時一併關閉底層串流。
/// </summary>
public static class ArchiveOpenHelper
{
    private const int PrefixScanMaxBytes = 1024 * 1024;
    private const int ZipTailScanMaxBytes = 65536 + 22;

    private static readonly byte[] ZipLocalHeaderSig = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] ZipEocdSig = [0x50, 0x4B, 0x05, 0x06];
    private static readonly byte[] Rar4Sig = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00];
    private static readonly byte[] Rar5Sig = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];
    private static readonly byte[] SevenZipSig = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    public static IArchive OpenArchive(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("Archive path cannot be empty.", nameof(archivePath));
        }

        try
        {
            return ArchiveFactory.OpenArchive(archivePath);
        }
        catch (Exception primaryEx) when (primaryEx is not FileNotFoundException
                                          and not DirectoryNotFoundException
                                          and not UnauthorizedAccessException)
        {
            if (!File.Exists(archivePath))
            {
                ExceptionDispatchInfo.Capture(primaryEx).Throw();
            }

            foreach (long baseOffset in FindCandidateSignatureOffsets(archivePath))
            {
                if (baseOffset <= 0)
                {
                    continue;
                }

                OffsetReadOnlyStream? offsetStream = null;
                try
                {
                    var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    offsetStream = new OffsetReadOnlyStream(fs, baseOffset);
                    return ArchiveFactory.OpenArchive(offsetStream, new ReaderOptions { LeaveStreamOpen = false });
                }
                catch
                {
                    offsetStream?.Dispose();
                }
            }

            ExceptionDispatchInfo.Capture(primaryEx).Throw();
            throw;
        }
    }

    private static List<long> FindCandidateSignatureOffsets(string archivePath)
    {
        var candidates = new List<long>();
        var seen = new HashSet<long>();

        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long fileLength = fs.Length;
        if (fileLength < 8)
        {
            return candidates;
        }

        // 1. ZIP EOCD 數學回推實際起點（可支援任意長度的前置垃圾資料或 SFX stub）
        TryAddZipEocdBaseOffset(fs, fileLength, candidates, seen);

        // 2. 掃描前段 1 MiB 內的標準壓縮檔簽章（ZIP / RAR4 / RAR5 / 7z）
        int prefixLen = (int)Math.Min(fileLength, PrefixScanMaxBytes);
        byte[] prefix = new byte[prefixLen];
        fs.Position = 0;
        int read = ReadExactOrPrefix(fs, prefix);

        for (int i = 1; i <= read - 4; i++)
        {
            if (prefix[i] == 0x50 && prefix.AsSpan(i).StartsWith(ZipLocalHeaderSig))
            {
                AddCandidate(i, candidates, seen);
                if (candidates.Count >= 8) break;
            }
            else if (prefix[i] == 0x52 && (prefix.AsSpan(i).StartsWith(Rar5Sig) || prefix.AsSpan(i).StartsWith(Rar4Sig)))
            {
                AddCandidate(i, candidates, seen);
                if (candidates.Count >= 8) break;
            }
            else if (prefix[i] == 0x37 && prefix.AsSpan(i).StartsWith(SevenZipSig))
            {
                AddCandidate(i, candidates, seen);
                if (candidates.Count >= 8) break;
            }
        }

        return candidates;
    }

    private static void TryAddZipEocdBaseOffset(FileStream fs, long fileLength, List<long> candidates, HashSet<long> seen)
    {
        if (fileLength < 22)
        {
            return;
        }

        int tailLen = (int)Math.Min(fileLength, ZipTailScanMaxBytes);
        long tailStart = fileLength - tailLen;
        byte[] tail = new byte[tailLen];
        fs.Position = tailStart;
        int read = ReadExactOrPrefix(fs, tail);

        Span<byte> sig = stackalloc byte[4];
        for (int i = read - 22; i >= 0; i--)
        {
            if (tail[i] != 0x50 || !tail.AsSpan(i).StartsWith(ZipEocdSig))
            {
                continue;
            }

            ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20, 2));
            long eocdPos = tailStart + i;
            if (eocdPos + 22 + commentLen > fileLength)
            {
                continue;
            }

            uint cdSize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 12, 4));
            uint cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 16, 4));
            long baseOffset = eocdPos - cdSize - cdOffset;
            if (baseOffset > 0 && baseOffset <= fileLength - 4)
            {
                fs.Position = baseOffset;
                if (fs.Read(sig) == 4 && (sig.SequenceEqual(ZipLocalHeaderSig) || sig.SequenceEqual(ZipEocdSig)))
                {
                    AddCandidate(baseOffset, candidates, seen);
                    return;
                }
            }
        }
    }

    private static int ReadExactOrPrefix(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer, total, buffer.Length - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private static void AddCandidate(long offset, List<long> candidates, HashSet<long> seen)
    {
        if (offset > 0 && seen.Add(offset))
        {
            candidates.Add(offset);
        }
    }

    private sealed class OffsetReadOnlyStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _baseOffset;
        private bool _disposed;

        public OffsetReadOnlyStream(Stream inner, long baseOffset)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (baseOffset < 0 || baseOffset > inner.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(baseOffset));
            }
            _baseOffset = baseOffset;
            _inner.Position = _baseOffset;
        }

        public override bool CanRead => !_disposed && _inner.CanRead;
        public override bool CanSeek => !_disposed && _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => Math.Max(0, _inner.Length - _baseOffset);

        public override long Position
        {
            get => Math.Max(0, _inner.Position - _baseOffset);
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_inner.Position < _baseOffset)
            {
                _inner.Position = _baseOffset;
            }
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_inner.Position < _baseOffset)
            {
                _inner.Position = _baseOffset;
            }
            return _inner.Read(buffer);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long len = Length;
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => len + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (target < 0)
            {
                throw new IOException("An attempt was made to move the position before the beginning of the stream.");
            }
            long actual = _inner.Seek(_baseOffset + target, SeekOrigin.Begin);
            return Math.Max(0, actual - _baseOffset);
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    _inner.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
