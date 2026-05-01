using ComicOrganizer.App.Core;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

/// <summary>
/// FolderRoleDetector 對常見漫畫庫結構的判定驗證。
/// 每個 test 在 Path.GetTempPath() 建一個獨立資料夾結構，跑完自動清。
/// </summary>
public class FolderRoleDetectorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ITestOutputHelper _output;

    public FolderRoleDetectorTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), "ComicOrgRoleTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    private string Mk(params string[] parts)
    {
        var p = parts.Length == 0 ? _tempRoot : Path.Combine(_tempRoot, Path.Combine(parts));
        Directory.CreateDirectory(p);
        return p;
    }

    private void Touch(string dir, string name, int bytes = 10)
    {
        File.WriteAllBytes(Path.Combine(dir, name), new byte[bytes]);
    }

    // ========== Case 1: Comic/作者/作品/01.jpg ==========
    [Fact]
    public void Library_Author_Work_Images()
    {
        var lib = Mk();   // _tempRoot 視為 Library
        var author = Mk("作者1");
        var work = Mk("作者1", "作品1");
        Touch(work, "01.jpg");
        Touch(work, "02.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(work));
        Assert.Equal(FolderRole.Author, d.Detect(author));
        Assert.Equal(FolderRole.Library, d.Detect(lib));
    }

    // ========== Case 2: Comic/分類/作者/作品/01.jpg ==========
    [Fact]
    public void Library_Category_Author_Work()
    {
        var lib = Mk();
        var category = Mk("不常看");
        var author = Mk("不常看", "作者1");
        var work = Mk("不常看", "作者1", "作品1");
        Touch(work, "01.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(work));
        Assert.Equal(FolderRole.Author, d.Detect(author));
        Assert.Equal(FolderRole.Category, d.Detect(category));
        Assert.Equal(FolderRole.Library, d.Detect(lib));
    }

    // ========== Author 含多個 zip 作品 ==========
    [Fact]
    public void Author_With_Only_Zips()
    {
        var lib = Mk();
        var author = Mk("[Flugel]");
        Touch(author, "作品A.zip");
        Touch(author, "作品B.zip");
        Touch(author, "作品C.cbz");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Author, d.Detect(author));
    }

    // ========== Author 混 zip + folder works ==========
    [Fact]
    public void Author_Mixed_Zips_And_Folders()
    {
        var lib = Mk();
        var author = Mk("作者");
        var workFolder = Mk("作者", "作品A");
        Touch(workFolder, "01.jpg");
        Touch(author, "作品B.zip");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(workFolder));
        Assert.Equal(FolderRole.Author, d.Detect(author));
    }

    // ========== Flat library: Comic/作品.zip 直接放 ==========
    [Fact]
    public void Library_Flat_With_Zips_Only()
    {
        var lib = Mk();
        Touch(lib, "作品A.zip");
        Touch(lib, "作品B.zip");

        var d = new FolderRoleDetector(lib);
        // Library root 路徑相等優先判定
        Assert.Equal(FolderRole.Library, d.Detect(lib));
    }

    // ========== 多層 Category: Comic/語言/中文/作者/作品 ==========
    [Fact]
    public void Nested_Categories()
    {
        var lib = Mk();
        var lang = Mk("語言");
        var zh = Mk("語言", "中文");
        var author = Mk("語言", "中文", "作者A");
        var work = Mk("語言", "中文", "作者A", "作品1");
        Touch(work, "01.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(work));
        Assert.Equal(FolderRole.Author, d.Detect(author));
        Assert.Equal(FolderRole.Category, d.Detect(zh));
        Assert.Equal(FolderRole.Category, d.Detect(lang));
        Assert.Equal(FolderRole.Library, d.Detect(lib));
    }

    // ========== Work 內含章節子目錄 (Comic/作者/作品/Ch1/01.jpg) ==========
    [Fact]
    public void Work_With_Chapter_Subfolders()
    {
        var lib = Mk();
        var author = Mk("作者");
        var work = Mk("作者", "作品");
        var ch1 = Mk("作者", "作品", "Ch1");
        var ch2 = Mk("作者", "作品", "Ch2");
        Touch(ch1, "01.jpg");
        Touch(ch2, "01.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(ch1));
        Assert.Equal(FolderRole.Work, d.Detect(ch2));
        // 作品 含 chapter (都是 Work) → 視為 Author 在邏輯上會錯
        // 設計上目前 chapter folder 仍標 Work，所以「作品」也是 Author。
        // 這是已知簡化（Work / Chapter 沒區分），未來真要區分時再加 heuristic。
        Assert.Equal(FolderRole.Author, d.Detect(work));
        Assert.Equal(FolderRole.Category, d.Detect(author));   // 作者 子是 Author（即「作品」）→ 退一層成 Category
    }

    // ========== Empty folder ==========
    [Fact]
    public void Empty_Folder_Is_Unknown()
    {
        var lib = Mk();
        var empty = Mk("空目錄");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Unknown, d.Detect(empty));
    }

    // ========== Single zip is Work ==========
    [Fact]
    public void Direct_Zip_File_Is_Work()
    {
        var lib = Mk();
        var author = Mk("作者");
        Touch(author, "作品.zip");
        var zipPath = Path.Combine(author, "作品.zip");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(zipPath));
    }

    // ========== Author 含 cover.jpg + subdir works → 仍是 Author ==========
    [Fact]
    public void Author_With_CoverImage_And_SubdirWorks()
    {
        var lib = Mk();
        var author = Mk("[Flugel]");
        Touch(author, "cover.jpg");          // banner / cover 不算 work
        var workA = Mk("[Flugel]", "作品A");
        var workB = Mk("[Flugel]", "作品B");
        for (int i = 1; i <= 10; i++) Touch(workA, $"{i:D2}.jpg");
        for (int i = 1; i <= 10; i++) Touch(workB, $"{i:D2}.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(workA));
        Assert.Equal(FolderRole.Work, d.Detect(workB));
        Assert.Equal(FolderRole.Author, d.Detect(author));   // 不再被 cover 誤判
    }

    // ========== Author 含 cover.jpg + zip 作品 → Author ==========
    [Fact]
    public void Author_With_CoverImage_And_ZipWorks()
    {
        var lib = Mk();
        var author = Mk("[Author]");
        Touch(author, "cover.jpg");
        Touch(author, "作品A.zip");
        Touch(author, "作品B.zip");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Author, d.Detect(author));
    }

    // ========== Work 含足夠頁面 (numeric naming) → 仍是 Work ==========
    [Fact]
    public void Work_With_Many_Numbered_Pages()
    {
        var lib = Mk();
        var author = Mk("作者");
        var work = Mk("作者", "作品");
        for (int i = 1; i <= 30; i++) Touch(work, $"{i:D3}.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(work));
        Assert.Equal(FolderRole.Author, d.Detect(author));
    }

    // ========== Work 只有 1 張圖 (極短/單頁) → 仍是 Work ==========
    [Fact]
    public void Work_With_Single_Image()
    {
        var lib = Mk();
        var work = Mk("作品");
        Touch(work, "image.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(work));
    }

    // ========== Work 含 Comic Market 編號 (即使有 chapter 子目錄) → 仍是 Work ==========
    [Fact]
    public void Work_With_CommentMarket_Number_Despite_Subdir()
    {
        var lib = Mk();
        var author = Mk("KOKIKKO");
        // 此 work folder 內含 chapter 子目錄
        var work = Mk("KOKIKKO", "[萌の空漢化社] (C76) (同人誌) [KOKIKKO] ATHENS");
        var chapter = Mk("KOKIKKO", "[萌の空漢化社] (C76) (同人誌) [KOKIKKO] ATHENS", "ATHENS");
        for (int i = 1; i <= 20; i++) Touch(chapter, $"{i:D2}.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(chapter));
        Assert.Equal(FolderRole.Work, d.Detect(work));     // (C76) + (同人誌) → 確定 Work
        Assert.Equal(FolderRole.Author, d.Detect(author));
    }

    // ========== Work 含「同人誌」標記 → Work ==========
    [Fact]
    public void Work_With_Doujinshi_Tag()
    {
        var lib = Mk();
        var work = Mk("[作者] 作品 (同人誌)");
        var sub = Mk("[作者] 作品 (同人誌)", "ch1");
        for (int i = 1; i <= 10; i++) Touch(sub, $"{i:D2}.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(work));
    }

    // ========== Work 含多個 [] (≥2) → Work ==========
    [Fact]
    public void Work_With_Multiple_Brackets()
    {
        var lib = Mk();
        var work = Mk("[Group] [Author] 作品 [中文]");
        var sub = Mk("[Group] [Author] 作品 [中文]", "ch1");
        for (int i = 1; i <= 10; i++) Touch(sub, $"{i:D2}.jpg");

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Work, d.Detect(work));
    }

    // ========== Author 命名只有 1 個 [] 不會誤判為 Work ==========
    [Fact]
    public void Author_With_SingleBracket_Not_Misclassified()
    {
        var lib = Mk();
        var author = Mk("[Flugel]");
        var workA = Mk("[Flugel]", "作品A");
        var workB = Mk("[Flugel]", "作品B");
        for (int i = 1; i <= 10; i++) { Touch(workA, $"{i:D2}.jpg"); Touch(workB, $"{i:D2}.jpg"); }

        var d = new FolderRoleDetector(lib);
        Assert.Equal(FolderRole.Author, d.Detect(author));
    }

    // ========== Cache 重複呼叫不重新計算 ==========
    [Fact]
    public void Detect_Caches_Result()
    {
        var lib = Mk();
        var author = Mk("作者");
        var work = Mk("作者", "作品");
        Touch(work, "01.jpg");

        var d = new FolderRoleDetector(lib);
        var r1 = d.Detect(work);
        // 刪掉 image 後再 detect 應拿到 cache 結果（仍 Work）
        File.Delete(Path.Combine(work, "01.jpg"));
        var r2 = d.Detect(work);
        Assert.Equal(r1, r2);
        Assert.Equal(FolderRole.Work, r1);
    }
}
