using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// FilenameMetadataParser 單元測試 — 涵蓋常見同人誌命名 pattern。
/// 失敗了不代表 parser 一定錯，可能只是新 pattern 該補。先建一條，未來補。
/// </summary>
public class FilenameMetadataParserTests
{
    // ===== ParseAuthorFolderName =====

    [Fact]
    public void Author_PlainName()
    {
        var (main, alias) = FilenameMetadataParser.ParseAuthorFolderName("Flugel");
        Assert.Equal("Flugel", main);
        Assert.Null(alias);
    }

    [Fact]
    public void Author_PureBracket()
    {
        var (main, alias) = FilenameMetadataParser.ParseAuthorFolderName("[小林優子]");
        Assert.Equal("小林優子", main);
        Assert.Null(alias);
    }

    [Fact]
    public void Author_BilingualBracket()
    {
        // 日文+羅馬拼音
        var (main, alias) = FilenameMetadataParser.ParseAuthorFolderName("タイガー[Tiger Roman]");
        Assert.Equal("タイガー", main);
        Assert.Equal("Tiger Roman", alias);
    }

    [Fact]
    public void Author_GroupArtistParens()
    {
        var (main, alias) = FilenameMetadataParser.ParseAuthorFolderName("Digital Lover (なかじまゆか)");
        Assert.Equal("Digital Lover", main);
        Assert.Equal("なかじまゆか", alias);
    }

    [Fact]
    public void Author_GroupArtistInBracket()
    {
        // "[Group (Author)]" — bracket 內的 (xxx) 變 alias
        var (main, alias) = FilenameMetadataParser.ParseAuthorFolderName("[ciaociao (あらきかなお)]");
        Assert.Equal("ciaociao", main);
        Assert.Equal("あらきかなお", alias);
    }

    // ===== ParseWorkName =====

    [Fact]
    public void Work_BasicWithEventAndLanguage()
    {
        var p = FilenameMetadataParser.ParseWorkName(
            "(C99) [Flugel] くぎづけはーと [中国翻訳][DL版].zip");

        Assert.Equal("Flugel", p.AuthorMainName);
        Assert.Null(p.AuthorAlias);
        Assert.Equal("くぎづけはーと", p.Title);
        Assert.Equal("zh-CN", p.Language);
        Assert.Contains(p.Tags, t => t.Name == "C99" && t.Category == "event");
        Assert.Contains(p.Tags, t => t.Name == "DL版");
        // 中国翻訳 應該變 Language 不在 Tags
        Assert.DoesNotContain(p.Tags, t => t.Name == "中国翻訳");
    }

    [Fact]
    public void Work_GroupArtistAndParody()
    {
        var p = FilenameMetadataParser.ParseWorkName(
            "[Digital Lover (なかじまゆか)] D.L.action 45 (オリジナル) [中国翻訳].zip");

        Assert.Equal("Digital Lover", p.AuthorMainName);
        Assert.Equal("なかじまゆか", p.AuthorAlias);
        Assert.Equal("D.L.action 45", p.Title);
        Assert.Equal("zh-CN", p.Language);
        Assert.Contains(p.Tags, t => t.Name == "オリジナル" && t.Category == "parody");
    }

    [Fact]
    public void Work_TranslationGroupDetection()
    {
        var p = FilenameMetadataParser.ParseWorkName(
            "(C92) [Flugel] Title [無邪気漢化組] [中国翻訳].zip");

        Assert.Equal("Flugel", p.AuthorMainName);
        Assert.Equal("無邪気漢化組", p.TranslationGroup);
        Assert.Equal("zh-CN", p.Language);
        // 翻譯組不該進 Tags
        Assert.DoesNotContain(p.Tags, t => t.Name == "無邪気漢化組");
    }

    [Fact]
    public void Work_DotsInTitle_NotEatenAsExtension()
    {
        // 過去 bug：Path.GetFileNameWithoutExtension 把 "D.L.action" 的 . 當副檔名
        // ParseWorkName 不能犯同樣錯
        var p = FilenameMetadataParser.ParseWorkName("[Author] D.L.action 45.zip");
        Assert.Equal("D.L.action 45", p.Title);
    }

    [Fact]
    public void Work_NoAuthorBlock_TitleOnly()
    {
        var p = FilenameMetadataParser.ParseWorkName("作品名.zip");
        Assert.Null(p.AuthorMainName);
        Assert.Equal("作品名", p.Title);
    }

    [Fact]
    public void Work_AuthorFromParent_SkipFilenameAuthor()
    {
        // 父目錄已是 Author，filename 開頭的 [Author] 應被當重複跳掉
        var p = FilenameMetadataParser.ParseWorkName(
            "[Flugel] Title [中国翻訳].zip", authorFromParent: "Flugel");
        Assert.Equal("Flugel", p.AuthorMainName);
        Assert.Null(p.AuthorAlias);
        Assert.Equal("Title", p.Title);
    }

    [Fact]
    public void Work_ChapterNoExtraction()
    {
        var p = FilenameMetadataParser.ParseWorkName("[Author] Title Vol.3.zip");
        Assert.Equal("Vol.3", p.ChapterNo);
        // ChapterNo 抽出但不從 title 移除（保留全名給使用者看）
        Assert.NotNull(p.Title);
        Assert.Contains("Vol.3", p.Title);
    }

    [Fact]
    public void Work_FolderNameAlsoWorks()
    {
        // ParseWorkName 也能吃資料夾名（沒副檔名）
        var p = FilenameMetadataParser.ParseWorkName(
            "[Flugel] くぎづけはーと [中文] [DL版]");
        Assert.Equal("Flugel", p.AuthorMainName);
        Assert.Equal("くぎづけはーと", p.Title);
        Assert.Equal("zh", p.Language);
        Assert.Contains(p.Tags, t => t.Name == "DL版");
    }

    [Fact]
    public void Work_MultipleEventsAndTags()
    {
        var p = FilenameMetadataParser.ParseWorkName(
            "(C103) [Group (Artist)] Some Title (Original Work) [English].zip");

        Assert.Equal("Group", p.AuthorMainName);
        Assert.Equal("Artist", p.AuthorAlias);
        Assert.Equal("Some Title", p.Title);
        Assert.Equal("en", p.Language);
        Assert.Contains(p.Tags, t => t.Name == "C103" && t.Category == "event");
        Assert.Contains(p.Tags, t => t.Name == "Original Work" && t.Category == "parody");
    }

    [Fact]
    public void Work_RarFile_AlsoStripsExtension()
    {
        var p = FilenameMetadataParser.ParseWorkName(
            "(C91) [HitenKei (Hiten)] R.E.I.N.A [無邪気漢化組][MJK-17-T438].rar");

        Assert.Equal("HitenKei", p.AuthorMainName);
        Assert.Equal("Hiten", p.AuthorAlias);
        Assert.Equal("R.E.I.N.A", p.Title);
        Assert.Equal("無邪気漢化組", p.TranslationGroup);
    }

    [Fact]
    public void Work_EmptyName_ReturnsEmpty()
    {
        var p = FilenameMetadataParser.ParseWorkName("");
        Assert.Null(p.Title);
        Assert.Null(p.AuthorMainName);
    }

    [Fact]
    public void Work_AuthorFromParent_LeadingLanguageBracketNotStripped()
    {
        // Case 2 from real library: parent author = "Cuvie", filename starts with [中文]
        // [中文] 不應被當作 author bracket 砍掉，應該當 language 解析
        var p = FilenameMetadataParser.ParseWorkName(
            "[中文][Cuvie]NIGHTMARE MAKER 5 夢魘製造機5",
            authorFromParent: "Cuvie");

        Assert.Equal("Cuvie", p.AuthorMainName);
        Assert.Equal("zh", p.Language);
        Assert.Equal("NIGHTMARE MAKER 5 夢魘製造機5", p.Title);
        // [Cuvie] 跟 author 同名應跳過、不變成 generic tag
        Assert.DoesNotContain(p.Tags, t => t.Name == "Cuvie");
    }

    [Fact]
    public void Work_AuthorFromParent_AuthorBracketDeduped()
    {
        // 父 author = "Flugel", filename `[Flugel] Title.zip`
        // [Flugel] 應被識別為 author dedupe，跳過、不變 tag
        var p = FilenameMetadataParser.ParseWorkName(
            "[Flugel] Title [DL版].zip",
            authorFromParent: "Flugel");

        Assert.Equal("Flugel", p.AuthorMainName);
        Assert.Equal("Title", p.Title);
        Assert.DoesNotContain(p.Tags, t => t.Name == "Flugel");
        Assert.Contains(p.Tags, t => t.Name == "DL版");
    }

    [Fact]
    public void Work_AuthorFromParent_GroupArtistBracketDeduped()
    {
        // 父 author = "Group", filename `[Group (Artist)] Title`
        // [Group (Artist)] 跟 "Group" 開頭一致 + ' ' 後綴 → 視為 author 重複
        var p = FilenameMetadataParser.ParseWorkName(
            "[Group (Artist)] Title.zip",
            authorFromParent: "Group");

        Assert.Equal("Group", p.AuthorMainName);
        Assert.Equal("Title", p.Title);
        Assert.DoesNotContain(p.Tags, t => t.Name.Contains("Group"));
    }
}
