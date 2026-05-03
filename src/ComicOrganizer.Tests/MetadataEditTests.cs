using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// SqliteAssetMetaStore 編輯 API 測試（#45 Phase 1）：
/// schema v4 migration / Author rename+merge / Tag rename+merge / Work tags / Author tags / 搜尋 / Usage 列表 / manually_edited flag。
/// </summary>
public class MetadataEditTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public MetadataEditTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_metaedit_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long SeedAsset(string path, long? authorId = null)
    {
        _store.Upsert(new AssetMeta { Path = path, Name = Path.GetFileName(path), IsFolder = false, FileSize = 1, Mtime = 1 });
        var id = _store.GetAssetId(path)!.Value;
        _store.UpsertMetadata(new AssetMetadata { AssetId = id, AuthorId = authorId, Title = "T", ParsedAt = 1 });
        return id;
    }

    // ===== Schema v4 / manually_edited =====

    [Fact]
    public void V4_AssetMetaHasManuallyEdited_DefaultsFalse()
    {
        var assetId = SeedAsset(@"C:\L\a.zip");
        var meta = _store.GetMetadata(assetId)!;
        Assert.False(meta.ManuallyEdited);

        _store.SetMetadataManuallyEdited(assetId, true);
        meta = _store.GetMetadata(assetId)!;
        Assert.True(meta.ManuallyEdited);
    }

    [Fact]
    public void UpsertMetadata_PreservesManuallyEdited()
    {
        var assetId = SeedAsset(@"C:\L\a.zip");
        _store.UpsertMetadata(new AssetMetadata { AssetId = assetId, Title = "Edited", ManuallyEdited = true, ParsedAt = 2 });
        var meta = _store.GetMetadata(assetId)!;
        Assert.True(meta.ManuallyEdited);
        Assert.Equal("Edited", meta.Title);
    }

    // ===== Asset tags：Disassociate / SetAssetTags =====

    [Fact]
    public void DisassociateTag_RemovesAssociation_KeepsTag()
    {
        var assetId = SeedAsset(@"C:\L\a.zip");
        var tagId = _store.UpsertTag("熱血");
        _store.AssociateTag(assetId, tagId);
        Assert.Single(_store.ListTagsForAsset(assetId));

        _store.DisassociateTag(assetId, tagId);
        Assert.Empty(_store.ListTagsForAsset(assetId));
        // tag 本身仍存在
        Assert.NotEmpty(_store.SearchTags("熱血"));
    }

    [Fact]
    public void SetAssetTags_ReplacesEntireSet()
    {
        var assetId = SeedAsset(@"C:\L\a.zip");
        _store.AssociateTag(assetId, _store.UpsertTag("熱血"));
        _store.AssociateTag(assetId, _store.UpsertTag("舊"));
        _store.SetAssetTags(assetId, new[] { "熱血", "王道", "異世界" });
        var tags = _store.ListTagsForAsset(assetId).Select(t => t.Name).ToHashSet();
        Assert.Equal(new HashSet<string> { "熱血", "王道", "異世界" }, tags);
    }

    // ===== Author tags =====

    [Fact]
    public void AuthorTags_CRUD()
    {
        var aId = _store.UpsertAuthor("Author1");
        var t1 = _store.UpsertTag("少年漫畫");
        var t2 = _store.UpsertTag("JUMP");
        _store.AssociateAuthorTag(aId, t1);
        _store.AssociateAuthorTag(aId, t2);
        Assert.Equal(2, _store.ListTagsForAuthor(aId).Count);

        _store.DisassociateAuthorTag(aId, t1);
        Assert.Single(_store.ListTagsForAuthor(aId));

        _store.SetAuthorTags(aId, new[] { "新 tag A", "新 tag B" });
        var names = _store.ListTagsForAuthor(aId).Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "新 tag A", "新 tag B" }, names);
    }

    // ===== Author rename / merge =====

    [Fact]
    public void RenameAuthor_Success()
    {
        var id = _store.UpsertAuthor("Old");
        _store.RenameAuthor(id, "New");
        Assert.NotNull(_store.FindAuthorByMainName("New"));
        Assert.Null(_store.FindAuthorByMainName("Old"));
    }

    [Fact]
    public void RenameAuthor_ConflictThrows()
    {
        var id1 = _store.UpsertAuthor("A");
        _store.UpsertAuthor("B");
        Assert.Throws<InvalidOperationException>(() => _store.RenameAuthor(id1, "B"));
        // 衝突後 id1 仍叫 A
        Assert.NotNull(_store.FindAuthorByMainName("A"));
    }

    [Fact]
    public void MergeAuthor_TransfersWorksAliasesTags_DeletesSrc()
    {
        var src = _store.UpsertAuthor("天狗");
        var dest = _store.UpsertAuthor("TENGU");
        _store.AddAuthorAlias(src, "Tengu-jp", "ja");
        var tagId = _store.UpsertTag("style:hardcore");
        _store.AssociateAuthorTag(src, tagId);

        var workId = SeedAsset(@"C:\L\Tengu\Work1.zip", src);

        _store.MergeAuthor(src, dest);

        // src 不存在
        Assert.Null(_store.FindAuthorByMainName("天狗"));
        // dest 仍在
        Assert.NotNull(_store.FindAuthorByMainName("TENGU"));
        // work 已歸屬 dest
        var meta = _store.GetMetadata(workId)!;
        Assert.Equal(dest, meta.AuthorId);
        // 別名（含 src 主名）併到 dest
        var aliases = _store.ListAuthorAliases(dest).Select(a => a.Name).OrderBy(n => n).ToList();
        Assert.Contains("天狗", aliases);
        Assert.Contains("Tengu-jp", aliases);
        // tag 也在 dest
        Assert.Contains(_store.ListTagsForAuthor(dest), t => t.Id == tagId);
    }

    // ===== Author alias edit / delete =====

    [Fact]
    public void UpdateAuthorAlias_ModifiesNameAndLang()
    {
        var aId = _store.UpsertAuthor("X");
        _store.AddAuthorAlias(aId, "Old", "en");
        var aliasId = _store.ListAuthorAliases(aId)[0].Id;
        _store.UpdateAuthorAlias(aliasId, "New", "ja");
        var alias = _store.ListAuthorAliases(aId)[0];
        Assert.Equal("New", alias.Name);
        Assert.Equal("ja", alias.Lang);
    }

    [Fact]
    public void DeleteAuthorAlias_RemovesEntry()
    {
        var aId = _store.UpsertAuthor("X");
        _store.AddAuthorAlias(aId, "ToRemove");
        var aliasId = _store.ListAuthorAliases(aId)[0].Id;
        _store.DeleteAuthorAlias(aliasId);
        Assert.Empty(_store.ListAuthorAliases(aId));
    }

    // ===== SetWorkAuthor =====

    [Fact]
    public void SetWorkAuthor_ChangesAuthorId()
    {
        var a1 = _store.UpsertAuthor("A1");
        var a2 = _store.UpsertAuthor("A2");
        var workId = SeedAsset(@"C:\L\W.zip", a1);
        _store.SetWorkAuthor(workId, a2);
        Assert.Equal(a2, _store.GetMetadata(workId)!.AuthorId);
        _store.SetWorkAuthor(workId, null);
        Assert.Null(_store.GetMetadata(workId)!.AuthorId);
    }

    // ===== Search APIs =====

    [Fact]
    public void SearchAuthors_PrefixMatchesMainNameOrAlias()
    {
        var a1 = _store.UpsertAuthor("Tengu");
        _store.AddAuthorAlias(a1, "天狗", "ja");
        _store.UpsertAuthor("Other");

        var byMain = _store.SearchAuthors("Ten").Select(a => a.MainName).ToList();
        Assert.Contains("Tengu", byMain);

        var byAlias = _store.SearchAuthors("天").Select(a => a.MainName).ToList();
        Assert.Contains("Tengu", byAlias);
    }

    [Fact]
    public void SearchTags_PrefixMatch()
    {
        _store.UpsertTag("熱血");
        _store.UpsertTag("熱血少年");
        _store.UpsertTag("溫馨");
        var matches = _store.SearchTags("熱").Select(t => t.Name).ToList();
        Assert.Equal(2, matches.Count);
        Assert.Contains("熱血", matches);
        Assert.Contains("熱血少年", matches);
    }

    // ===== Tag rename / merge / delete / category =====

    [Fact]
    public void RenameTag_Success()
    {
        var id = _store.UpsertTag("old");
        _store.RenameTag(id, "renamed");
        Assert.Single(_store.SearchTags("renamed"));
        Assert.Empty(_store.SearchTags("old"));
    }

    [Fact]
    public void RenameTag_ConflictThrows()
    {
        var id1 = _store.UpsertTag("a");
        _store.UpsertTag("b");
        Assert.Throws<InvalidOperationException>(() => _store.RenameTag(id1, "b"));
    }

    [Fact]
    public void MergeTag_TransfersAssociations()
    {
        var srcTag = _store.UpsertTag("漢化");
        var destTag = _store.UpsertTag("中文");
        var workId = SeedAsset(@"C:\L\W.zip");
        var authorId = _store.UpsertAuthor("Author");
        _store.AssociateTag(workId, srcTag);
        _store.AssociateAuthorTag(authorId, srcTag);

        _store.MergeTag(srcTag, destTag);

        // src tag 已刪
        Assert.Empty(_store.SearchTags("漢化"));
        // 關聯轉到 dest
        Assert.Contains(_store.ListTagsForAsset(workId), t => t.Id == destTag);
        Assert.Contains(_store.ListTagsForAuthor(authorId), t => t.Id == destTag);
    }

    [Fact]
    public void DeleteTag_CascadesAssociations()
    {
        var tagId = _store.UpsertTag("temp");
        var workId = SeedAsset(@"C:\L\W.zip");
        var authorId = _store.UpsertAuthor("A");
        _store.AssociateTag(workId, tagId);
        _store.AssociateAuthorTag(authorId, tagId);

        _store.DeleteTag(tagId);

        Assert.Empty(_store.ListTagsForAsset(workId));
        Assert.Empty(_store.ListTagsForAuthor(authorId));
    }

    [Fact]
    public void SetTagCategory_Updates()
    {
        var id = _store.UpsertTag("少年");
        _store.SetTagCategory(id, "genre");
        var t = _store.SearchTags("少年")[0];
        Assert.Equal("genre", t.Category);
    }

    // ===== Usage 列表 =====

    [Fact]
    public void ListTagsByUsage_CountsAssetAndAuthor()
    {
        var t1 = _store.UpsertTag("A");
        var t2 = _store.UpsertTag("B");
        var workId = SeedAsset(@"C:\L\W.zip");
        var authorId = _store.UpsertAuthor("Author");
        _store.AssociateTag(workId, t1);
        _store.AssociateAuthorTag(authorId, t1);
        _store.AssociateAuthorTag(authorId, t2);

        var usage = _store.ListTagsByUsage().ToDictionary(u => u.Name);
        Assert.Equal(1, usage["A"].AssetCount);
        Assert.Equal(1, usage["A"].AuthorCount);
        Assert.Equal(2, usage["A"].TotalCount);
        Assert.Equal(0, usage["B"].AssetCount);
        Assert.Equal(1, usage["B"].AuthorCount);
    }

    [Fact]
    public void ListAuthorsByUsage_CountsWorksAliasesTags()
    {
        var aId = _store.UpsertAuthor("Author1");
        _store.AddAuthorAlias(aId, "Alias1");
        _store.AddAuthorAlias(aId, "Alias2");
        var t = _store.UpsertTag("tagX");
        _store.AssociateAuthorTag(aId, t);
        SeedAsset(@"C:\L\W1.zip", aId);
        SeedAsset(@"C:\L\W2.zip", aId);

        var usage = _store.ListAuthorsByUsage().First(u => u.Author.Id == aId);
        Assert.Equal(2, usage.WorkCount);
        Assert.Equal(2, usage.AliasCount);
        Assert.Equal(1, usage.TagCount);
    }
}
