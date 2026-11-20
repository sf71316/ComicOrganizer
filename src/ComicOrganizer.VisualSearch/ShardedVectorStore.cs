using Cloud.Unum.USearch;

namespace ComicOrganizer.VisualSearch;

/// <summary>
/// 分片式 USearch Int8 向量索引管理（預設每 500,000 筆向量切一個分片）。
/// 支援唯讀 MMap 並行查詢合併、增量追加、單筆移除與單分片重整（Compact）。
/// </summary>
public sealed class ShardedVectorStore : IDisposable
{
    public const int DefaultMaxVectorsPerShard = 500_000;

    private readonly VisualSearchPaths _paths;
    private readonly int _dimensions;
    private readonly int _maxVectorsPerShard;
    private readonly bool _readOnlyMmap;
    private readonly Dictionary<int, USearchIndex> _shards = new();
    private readonly HashSet<int> _dirtyShards = new();
    private int _activeWriteShardId;

    public ShardedVectorStore(
        VisualSearchPaths paths,
        int dimensions = 512,
        bool readOnlyMmap = false,
        int maxVectorsPerShard = DefaultMaxVectorsPerShard)
    {
        _paths = paths;
        _dimensions = dimensions;
        _readOnlyMmap = readOnlyMmap;
        _maxVectorsPerShard = Math.Max(1, maxVectorsPerShard);

        if (!_readOnlyMmap)
        {
            _paths.EnsureDirectories();
        }

        LoadExistingShards();
    }

    public int ShardCount => _shards.Count;

    public int CurrentWriteShardId
    {
        get
        {
            if (_readOnlyMmap)
                throw new InvalidOperationException("唯讀 MMap 模式不支援寫入。");
            EnsureWritableShardCapacity();
            return _activeWriteShardId;
        }
    }

    private void LoadExistingShards()
    {
        if (!Directory.Exists(_paths.ShardsDirectory)) return;

        var files = Directory.GetFiles(_paths.ShardsDirectory, "panels_*.usearch")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        int maxId = -1;
        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stem.Length > 7 && int.TryParse(stem[7..], out int shardId))
            {
                _shards[shardId] = new USearchIndex(file, view: _readOnlyMmap);
                if (shardId > maxId) maxId = shardId;
            }
        }

        _activeWriteShardId = maxId >= 0 ? maxId : 0;
    }

    private void EnsureWritableShardCapacity()
    {
        if (!_shards.TryGetValue(_activeWriteShardId, out var current))
        {
            current = new USearchIndex(MetricKind.Cos, ScalarKind.Int8, (ulong)_dimensions);
            _shards[_activeWriteShardId] = current;
            _dirtyShards.Add(_activeWriteShardId);
            return;
        }

        if ((long)current.Size() >= _maxVectorsPerShard)
        {
            // 先把已滿的舊分片存檔，再開啟下一個新分片
            if (_dirtyShards.Remove(_activeWriteShardId))
            {
                current.Save(_paths.GetShardPath(_activeWriteShardId));
            }
            _activeWriteShardId++;
            _shards[_activeWriteShardId] = new USearchIndex(MetricKind.Cos, ScalarKind.Int8, (ulong)_dimensions);
            _dirtyShards.Add(_activeWriteShardId);
        }
    }

    public void Add(int shardId, ulong vecId, float[] vector)
    {
        if (_readOnlyMmap)
            throw new InvalidOperationException("唯讀 MMap 模式不支援寫入。");

        if (!_shards.TryGetValue(shardId, out var idx))
        {
            idx = new USearchIndex(MetricKind.Cos, ScalarKind.Int8, (ulong)_dimensions);
            _shards[shardId] = idx;
        }
        idx.Add(vecId, vector);
        _dirtyShards.Add(shardId);
    }

    public void Remove(int shardId, ulong vecId)
    {
        if (_readOnlyMmap)
            throw new InvalidOperationException("唯讀 MMap 模式不支援移除。");

        if (_shards.TryGetValue(shardId, out var idx))
        {
            idx.Remove(vecId);
            _dirtyShards.Add(shardId);
        }
    }

    /// <summary>
    /// 跨分片搜尋並合併 Top-K（依 Cosine Distance 由小到大排序）。
    /// </summary>
    public List<(ulong VecId, float Distance)> SearchAllShards(float[] queryVector, int topK)
    {
        if (_shards.Count == 0) return [];

        var merged = new List<(ulong VecId, float Distance)>(_shards.Count * topK);
        foreach (var idx in _shards.Values)
        {
            if (idx.Size() == 0) continue;
            int found = idx.Search(queryVector, topK, out var keys, out var dists);
            for (int i = 0; i < found; i++)
            {
                merged.Add((keys[i], dists[i]));
            }
        }

        merged.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        if (merged.Count > topK)
        {
            merged.RemoveRange(topK, merged.Count - topK);
        }
        return merged;
    }

    /// <summary>
    /// 重整指定分片：僅將仍有效的 activeVecIds 複製到新分片並原子覆寫磁碟檔，徹底回收 tombstone 空間。
    /// </summary>
    public int CompactShard(int shardId, IReadOnlyList<long> activeVecIds)
    {
        if (_readOnlyMmap)
            throw new InvalidOperationException("唯讀 MMap 模式不支援分片重整。");

        if (!_shards.TryGetValue(shardId, out var oldIdx))
            return 0;

        var newIdx = new USearchIndex(MetricKind.Cos, ScalarKind.Int8, (ulong)_dimensions);
        int copied = 0;
        foreach (var id in activeVecIds)
        {
            ulong key = (ulong)id;
            if (!oldIdx.Contains(key)) continue;
            int got = oldIdx.Get(key, out float[] vecBuf);
            if (got > 0 && vecBuf != null && vecBuf.Length == _dimensions)
            {
                newIdx.Add(key, vecBuf);
                copied++;
            }
        }

        // 先釋放舊句柄再覆寫 Windows 檔案
        oldIdx.Dispose();
        var targetPath = _paths.GetShardPath(shardId);
        var tempPath = targetPath + ".compact";
        newIdx.Save(tempPath);
        File.Move(tempPath, targetPath, overwrite: true);

        _shards[shardId] = newIdx;
        _dirtyShards.Remove(shardId);
        return copied;
    }

    public void SaveDirtyShards()
    {
        if (_readOnlyMmap) return;

        foreach (var shardId in _dirtyShards.ToList())
        {
            if (_shards.TryGetValue(shardId, out var idx))
            {
                var path = _paths.GetShardPath(shardId);
                var tmp = path + ".tmp";
                idx.Save(tmp);
                File.Move(tmp, path, overwrite: true);
            }
        }
        _dirtyShards.Clear();
    }

    public void Dispose()
    {
        foreach (var idx in _shards.Values)
        {
            idx.Dispose();
        }
        _shards.Clear();
        _dirtyShards.Clear();
    }
}
