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
        // 展場場次與 DL版 / 中国翻訳 僅剝除以淨化標題，均不寫入 Tags
        Assert.DoesNotContain(p.Tags, t => t.Name == "C99");
        Assert.DoesNotContain(p.Tags, t => t.Name == "DL版");
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
        Assert.DoesNotContain(p.Tags, t => t.Name == "DL版");
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
        Assert.DoesNotContain(p.Tags, t => t.Name == "C103");
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
        // 父 author = "Flugel", filename `[Flugel] Title [DL版].zip`
        // [Flugel] 與 [DL版] 皆剝除且不變 tag
        var p = FilenameMetadataParser.ParseWorkName(
            "[Flugel] Title [DL版].zip",
            authorFromParent: "Flugel");

        Assert.Equal("Flugel", p.AuthorMainName);
        Assert.Equal("Title", p.Title);
        Assert.DoesNotContain(p.Tags, t => t.Name == "Flugel" || t.Name == "DL版");
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

    [Fact]
    public void Work_TouhouReitaisaiAndTraditionalChinese()
    {
        var p = FilenameMetadataParser.ParseWorkName("(例大祭15) [Circle (Artist)] Title [中國翻譯].zip");

        Assert.Equal("Circle", p.AuthorMainName);
        Assert.Equal("Artist", p.AuthorAlias);
        Assert.Equal("Title", p.Title);
        Assert.Equal("zh-TW", p.Language);
        Assert.Null(p.TranslationGroup);
        Assert.DoesNotContain(p.Tags, t => t.Name == "例大祭15");
        Assert.DoesNotContain(p.Tags, t => t.Name == "中國翻譯");
    }

    [Fact]
    public void Work_VariousConventionEvents_StrippedFromTitleAndOmittedFromTags()
    {
        var conventions = new[]
        {
            ("(東方紅楼夢15) [Circle (Artist)] Title 1.zip", "東方紅楼夢15", "Title 1"),
            ("(紅樓夢16) [Circle (Artist)] Title 2.zip", "紅樓夢16", "Title 2"),
            ("(サンクリ2022) [Circle (Artist)] Title 3.zip", "サンクリ2022", "Title 3"),
            ("(SC2020) [Circle (Artist)] Title 4.zip", "SC2020", "Title 4"),
            ("(FF40) [Circle (Artist)] Title 5.zip", "FF40", "Title 5"),
            ("(PF35) [Circle (Artist)] Title 6.zip", "PF35", "Title 6"),
            ("(こみトレ30) [Circle (Artist)] Title 7.zip", "こみトレ30", "Title 7"),
        };

        foreach (var (filename, omittedEvent, expectedTitle) in conventions)
        {
            var p = FilenameMetadataParser.ParseWorkName(filename);
            Assert.Equal("Circle", p.AuthorMainName);
            Assert.Equal("Artist", p.AuthorAlias);
            Assert.Equal(expectedTitle, p.Title);
            Assert.DoesNotContain(p.Tags, t => t.Name == omittedEvent);
        }
    }

    [Fact]
    public void Work_VariousChineseAndJapaneseLanguageVariants()
    {
        var cases = new[]
        {
            ("[Author] Title [簡中].zip", "zh-CN"),
            ("[Author] Title [簡體中文].zip", "zh-CN"),
            ("[Author] Title [简体中文].zip", "zh-CN"),
            ("[Author] Title [繁中].zip", "zh-TW"),
            ("[Author] Title [繁體中文].zip", "zh-TW"),
            ("[Author] Title [中国語].zip", "zh"),
            ("[Author] Title [中國語].zip", "zh"),
            ("[Author] Title [日本語].zip", "ja"),
            ("[Author] Title [中文].zip", "zh"),
            ("[Author] Title [中国翻訳].zip", "zh-CN"),
            ("[Author] Title [中譯].zip", "zh-TW"),
        };

        foreach (var (filename, expectedLang) in cases)
        {
            var p = FilenameMetadataParser.ParseWorkName(filename);
            Assert.Equal(expectedLang, p.Language);
            Assert.Null(p.TranslationGroup);
        }
    }

    [Fact]
    public void Work_FormatEventLanguageAfterOtherPrefixes_AndParodyNormalization()
    {
        // (同人誌) + (C90) + 漢化組前置 + 艦これ無空白副標 + (Chinese) 不得污染 parody
        var p1 = FilenameMetadataParser.ParseWorkName(
            "(同人誌) (C90) [Rosapersica (一ノ宮)] ヨルヤハギ5 (艦隊これくしょん-艦これ-) [中国翻訳] [嗶咔嗶咔漢化組].zip");
        Assert.Equal("Rosapersica", p1.AuthorMainName);
        Assert.Equal("一ノ宮", p1.AuthorAlias);
        Assert.Equal("ヨルヤハギ5", p1.Title);
        Assert.Equal("zh-CN", p1.Language);
        Assert.Equal("嗶咔嗶咔漢化組", p1.TranslationGroup);
        Assert.DoesNotContain(p1.Tags, t => t.Name == "C90" || t.Name == "同人誌");
        Assert.Contains(p1.Tags, t => t.Name == "艦隊これくしょん -艦これ-" && t.Category == "parody");

        var p2 = FilenameMetadataParser.ParseWorkName(
            "[Angiris Council漢化组] (C97) [Rosapersica (一ノ宮)] ヨルヤハギ13 (艦隊これくしょん -艦これ-) (Chinese) [無修正][Digital].zip");
        Assert.Equal("Rosapersica", p2.AuthorMainName);
        Assert.Equal("一ノ宮", p2.AuthorAlias);
        Assert.Equal("ヨルヤハギ13", p2.Title);
        Assert.Equal("Angiris Council漢化组", p2.TranslationGroup);
        Assert.Equal("zh", p2.Language);
        Assert.DoesNotContain(p2.Tags, t => t.Name == "C97"
            || t.Name == "無修正"
            || t.Name == "Digital"
            || t.Name.Equals("Chinese", StringComparison.OrdinalIgnoreCase));

        var p3 = FilenameMetadataParser.ParseWorkName(
            "[Author] Title (成年コミック) (C87) (FateGrand Order) [CH][CE家族社].zip");
        Assert.Equal("zh", p3.Language);
        Assert.Equal("CE家族社", p3.TranslationGroup);
        Assert.Contains(p3.Tags, t => t.Name == "Fate Grand Order" && t.Category == "parody");
        Assert.DoesNotContain(p3.Tags, t => t.Name is "C87" or "成年コミック" or "CH" or "CE家族社");
    }

    [Fact]
    public void Work_AuthorFromParent_CircleOrJapanesePenName_NotLeakedAsTag()
    {
        var p1 = FilenameMetadataParser.ParseWorkName(
            "(C90) [Cat FooD (なぱた)] 作品標題 (オリジナル) [中文].zip",
            authorFromParent: "Napata");
        Assert.Equal("Napata", p1.AuthorMainName);
        Assert.False(string.IsNullOrWhiteSpace(p1.AuthorAlias));
        Assert.Equal("作品標題", p1.Title);
        Assert.DoesNotContain(p1.Tags, t => t.Name.Contains("なぱた") || t.Name.Contains("Cat FooD"));
        Assert.Contains(p1.Tags, t => t.Name == "オリジナル" && t.Category == "parody");

        var p2 = FilenameMetadataParser.ParseWorkName(
            "[Rosapersica (一ノ宮)] ヨルヤハギ2 (艦隊これくしょん -艦これ-)",
            authorFromParent: "一ノ宮");
        Assert.Equal("一ノ宮", p2.AuthorMainName);
        Assert.Equal("Rosapersica", p2.AuthorAlias);
        Assert.DoesNotContain(p2.Tags, t => t.Name.Contains("Rosapersica"));

        // 開頭是規格詞 [DL版] 時不可當成作者括號吃掉，且不產生 DL版 或 Flugel 標籤
        var p3 = FilenameMetadataParser.ParseWorkName(
            "[DL版] 標題 [Flugel].zip",
            authorFromParent: "Flugel");
        Assert.Equal("Flugel", p3.AuthorMainName);
        Assert.Equal("標題", p3.Title);
        Assert.DoesNotContain(p3.Tags, t => t.Name == "DL版" || t.Name == "Flugel");
    }

    [Fact]
    public void Work_ExpandedNoiseLanguageMagazineAndLeadingSpecFilters()
    {
        var p1 = FilenameMetadataParser.ParseWorkName(
            "[無修正] [大鸟可不敢乱转汉化] [ae] 月と向日葵 (COMlC 快楽天 2017年9月号) [中国翻译] [薄碼] [V2] [01].zip");
        Assert.Equal("ae", p1.AuthorMainName);
        Assert.Equal("大鸟可不敢乱转汉化", p1.TranslationGroup);
        Assert.Equal("zh-CN", p1.Language);
        Assert.Equal("月と向日葵", p1.Title);
        Assert.Empty(p1.Tags);

        var p2 = FilenameMetadataParser.ParseWorkName(
            "[宵野コタロー] テイクアウトハニー (コミックホットミルク 2017年9月号) (同人CG集) [日翻中] [風與黑暗掃圖] [完結] [2009-04-30].zip");
        Assert.Equal("宵野コタロー", p2.AuthorMainName);
        Assert.Equal("zh", p2.Language);
        Assert.Equal("風與黑暗掃圖", p2.TranslationGroup);
        Assert.Equal("テイクアウトハニー", p2.Title);
        Assert.Empty(p2.Tags);

        // macOS NFD 分解假名 `きくらけ\u3099屋` 與父目錄 NFC `きくらげ屋` 應視為同一作者並消除重複 tag
        var p3 = FilenameMetadataParser.ParseWorkName(
            "[きくらけ\u3099屋(きくらけ\u3099)] 作品.zip",
            authorFromParent: "きくらげ屋");
        Assert.Equal("きくらげ屋", p3.AuthorMainName);
        Assert.Equal("作品", p3.Title);
        Assert.Empty(p3.Tags);
    }
}
