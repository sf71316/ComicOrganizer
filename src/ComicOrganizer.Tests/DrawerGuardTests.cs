using ComicOrganizer.App.Core;
using ComicOrganizer.App.UI;
using Xunit;

namespace ComicOrganizer.Tests;

public class DrawerGuardTests
{
    private static WorkDetail CreateSampleWorkDetail() => new()
    {
        AssetId = 1,
        Path = @"E:\Comics\Author\Work1",
        Name = "Work1",
        Title = "Sample Title",
        AuthorId = 10,
        AuthorMainName = "AuthorA",
        Series = "SeriesX",
        ChapterNo = "Ch.1",
        Language = "zh-TW",
        TranslationGroup = "GroupA",
        Rating = 4,
        Tags = new List<Tag>
        {
            new() { Id = 1, Name = "comedy", Category = "genre" },
            new() { Id = 2, Name = "romance", Category = "genre" },
        },
    };

    private static AuthorInfo CreateSampleAuthorInfo() => new()
    {
        Author = new Author { Id = 10, MainName = "AuthorA", FolderPath = @"E:\Comics\AuthorA" },
        Aliases = new List<AuthorAlias>
        {
            new() { Id = 1, AuthorId = 10, Name = "AliasOne", Lang = "en" },
            new() { Id = 2, AuthorId = 10, Name = "別名二", Lang = null },
        },
        AuthorTags = new List<Tag>
        {
            new() { Id = 11, Name = "full color", Category = "meta" },
            new() { Id = 12, Name = "comedy", Category = "genre" },
        },
    };

    [Fact]
    public void WorkEditChanges_UnchangedOrWhitespaceNormalized_ReturnsFalse()
    {
        var detail = CreateSampleWorkDetail();
        var snapshot = new WorkEditSnapshot(
            Title: "  Sample Title ",
            AuthorName: "AuthorA",
            Series: "SeriesX",
            ChapterNo: "Ch.1",
            Language: "zh-TW",
            TranslationGroup: "GroupA",
            Rating: 4,
            Tags: new[] { "ROMANCE", "comedy" },
            PendingTagInput: "   ");

        Assert.False(DrawerEditStateComparer.HasWorkEditChanges(detail, snapshot));
    }

    [Fact]
    public void WorkEditChanges_NullDetail_ReturnsFalse()
    {
        var snapshot = new WorkEditSnapshot("Title", null, null, null, null, null, null, Array.Empty<string>());
        Assert.False(DrawerEditStateComparer.HasWorkEditChanges(null, snapshot));
    }

    [Fact]
    public void WorkEditChanges_NullVsZeroRatingAndEmptyStrings_ReturnsFalse()
    {
        var detail = new WorkDetail
        {
            Path = "p",
            Name = "n",
            Title = null,
            AuthorMainName = "",
            Series = null,
            ChapterNo = null,
            Language = null,
            TranslationGroup = null,
            Rating = null,
            Tags = new List<Tag>(),
        };
        var snapshot = new WorkEditSnapshot(
            Title: "",
            AuthorName: "  ",
            Series: "",
            ChapterNo: "",
            Language: "",
            TranslationGroup: "",
            Rating: 0,
            Tags: Array.Empty<string>(),
            PendingTagInput: "");

        Assert.False(DrawerEditStateComparer.HasWorkEditChanges(detail, snapshot));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("author")]
    [InlineData("series")]
    [InlineData("chapter")]
    [InlineData("language")]
    [InlineData("group")]
    [InlineData("rating")]
    [InlineData("tag_added")]
    [InlineData("tag_removed")]
    [InlineData("pending_tag")]
    public void WorkEditChanges_AnyFieldModified_ReturnsTrue(string field)
    {
        var detail = CreateSampleWorkDetail();
        var snapshot = new WorkEditSnapshot(
            Title: field == "title" ? "Modified Title" : detail.Title,
            AuthorName: field == "author" ? "AuthorB" : detail.AuthorMainName,
            Series: field == "series" ? "SeriesY" : detail.Series,
            ChapterNo: field == "chapter" ? "Ch.2" : detail.ChapterNo,
            Language: field == "language" ? "ja" : detail.Language,
            TranslationGroup: field == "group" ? "" : detail.TranslationGroup,
            Rating: field == "rating" ? 5 : detail.Rating,
            Tags: field switch
            {
                "tag_added" => new[] { "comedy", "romance", "action" },
                "tag_removed" => new[] { "comedy" },
                _ => new[] { "comedy", "romance" },
            },
            PendingTagInput: field == "pending_tag" ? "unsubmitted" : "");

        Assert.True(DrawerEditStateComparer.HasWorkEditChanges(detail, snapshot));
    }

    [Fact]
    public void AuthorEditChanges_Unchanged_ReturnsFalse()
    {
        var info = CreateSampleAuthorInfo();
        var snapshot = new AuthorEditSnapshot(
            MainName: " AuthorA ",
            Aliases: new[]
            {
                new AliasSnapshot("別名二", ""),
                new AliasSnapshot("AliasOne", "en"),
            },
            Tags: new[] { "COMEDY", "full color" },
            PendingAliasName: " ",
            PendingAliasLang: "",
            PendingTagInput: null);

        Assert.False(DrawerEditStateComparer.HasAuthorEditChanges(info, snapshot));
    }

    [Theory]
    [InlineData("main_name")]
    [InlineData("alias_added")]
    [InlineData("alias_removed")]
    [InlineData("alias_lang_changed")]
    [InlineData("tag_added")]
    [InlineData("tag_removed")]
    [InlineData("pending_alias_name")]
    [InlineData("pending_alias_lang")]
    [InlineData("pending_tag")]
    public void AuthorEditChanges_AnyFieldModified_ReturnsTrue(string field)
    {
        var info = CreateSampleAuthorInfo();
        var aliases = field switch
        {
            "alias_added" => new[]
            {
                new AliasSnapshot("AliasOne", "en"),
                new AliasSnapshot("別名二", null),
                new AliasSnapshot("NewAlias", "ja"),
            },
            "alias_removed" => new[]
            {
                new AliasSnapshot("AliasOne", "en"),
            },
            "alias_lang_changed" => new[]
            {
                new AliasSnapshot("AliasOne", "ja"),
                new AliasSnapshot("別名二", null),
            },
            _ => new[]
            {
                new AliasSnapshot("AliasOne", "en"),
                new AliasSnapshot("別名二", null),
            },
        };

        var tags = field switch
        {
            "tag_added" => new[] { "full color", "comedy", "solo" },
            "tag_removed" => new[] { "comedy" },
            _ => new[] { "full color", "comedy" },
        };

        var snapshot = new AuthorEditSnapshot(
            MainName: field == "main_name" ? "AuthorB" : info.Author.MainName,
            Aliases: aliases,
            Tags: tags,
            PendingAliasName: field == "pending_alias_name" ? "typing" : "",
            PendingAliasLang: field == "pending_alias_lang" ? "ja" : "",
            PendingTagInput: field == "pending_tag" ? "typing" : "");

        Assert.True(DrawerEditStateComparer.HasAuthorEditChanges(info, snapshot));
    }

    [Theory]
    [InlineData(DrawerOutsideClickEvaluator.WM_LBUTTONDOWN)]
    [InlineData(DrawerOutsideClickEvaluator.WM_RBUTTONDOWN)]
    [InlineData(DrawerOutsideClickEvaluator.WM_NCLBUTTONDOWN)]
    public void OutsideClickEvaluator_OwnerClick_ConsumesAndTriggersAsyncClose(int msg)
    {
        var eval = new DrawerOutsideClickEvaluator();
        var decision = eval.ProcessMessage(
            msg,
            drawerDisposed: false,
            () => DrawerClickTarget.Owner,
            isPrompting: false,
            nowTickMs: 1000);

        Assert.True(decision.Consume);
        Assert.True(decision.ShouldTriggerOutsideClick);
        Assert.False(decision.ShouldDetach);
    }

    [Theory]
    [InlineData(DrawerClickTarget.Drawer)]
    [InlineData(DrawerClickTarget.OtherPopup)]
    public void OutsideClickEvaluator_DrawerOrOtherPopup_DoesNotConsumeOrClose(DrawerClickTarget target)
    {
        var eval = new DrawerOutsideClickEvaluator();
        var decision = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_LBUTTONDOWN,
            drawerDisposed: false,
            () => target,
            isPrompting: false,
            nowTickMs: 1000);

        Assert.False(decision.Consume);
        Assert.False(decision.ShouldTriggerOutsideClick);
        Assert.False(decision.ShouldDetach);
    }

    [Fact]
    public void OutsideClickEvaluator_RightClickOnOwner_SwallowsButtonUpEvenAfterDrawerClosed()
    {
        var eval = new DrawerOutsideClickEvaluator();

        // 1. Right mouse down on Owner -> consumed, schedules close
        var down = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_RBUTTONDOWN,
            drawerDisposed: false,
            () => DrawerClickTarget.Owner,
            isPrompting: false,
            nowTickMs: 1000);
        Assert.True(down.Consume);
        Assert.True(down.ShouldTriggerOutsideClick);

        // 2. Async callback runs and closes drawer BEFORE user releases right mouse button
        eval.MarkQueuedWorkCompleted();
        eval.MarkDrawerClosed();
        Assert.True(eval.HasPendingButtonUp(1050));

        // 3. WM_RBUTTONUP arrives on Owner -> MUST still be consumed so WinForms MouseUp ContextMenu won't open
        var up = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_RBUTTONUP,
            drawerDisposed: true,
            () => DrawerClickTarget.Owner,
            isPrompting: false,
            nowTickMs: 1080);
        Assert.True(up.Consume);
        Assert.False(up.ShouldTriggerOutsideClick);
        Assert.True(up.ShouldDetach);
        Assert.False(eval.HasPendingButtonUp(1080));
    }

    [Fact]
    public void OutsideClickEvaluator_ReentrancyGuard_BlocksDuplicatePromptWhileQueuedOrPrompting()
    {
        var eval = new DrawerOutsideClickEvaluator();

        // First click queues close/prompt
        var first = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_LBUTTONDOWN,
            drawerDisposed: false,
            () => DrawerClickTarget.Owner,
            isPrompting: false,
            nowTickMs: 1000);
        Assert.True(first.Consume);
        Assert.True(first.ShouldTriggerOutsideClick);

        // Second rapid click before queued callback runs -> still consumed, no second trigger
        var secondBeforeRun = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_LBUTTONDBLCLK,
            drawerDisposed: false,
            () => DrawerClickTarget.Owner,
            isPrompting: false,
            nowTickMs: 1020);
        Assert.True(secondBeforeRun.Consume);
        Assert.False(secondBeforeRun.ShouldTriggerOutsideClick);

        // Queued callback ran and opened Modal (isPrompting == true) -> click on owner does not re-trigger
        eval.MarkQueuedWorkCompleted();
        var whilePrompting = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_LBUTTONDOWN,
            drawerDisposed: false,
            () => DrawerClickTarget.Owner,
            isPrompting: true,
            nowTickMs: 1200);
        Assert.True(whilePrompting.Consume);
        Assert.False(whilePrompting.ShouldTriggerOutsideClick);

        // Click on Modal itself passes through freely
        var modalClick = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_LBUTTONDOWN,
            drawerDisposed: false,
            () => DrawerClickTarget.OtherPopup,
            isPrompting: true,
            nowTickMs: 1300);
        Assert.False(modalClick.Consume);
        Assert.False(modalClick.ShouldTriggerOutsideClick);

        // User picked "繼續編輯" -> isPrompting back to false -> next Owner click triggers prompt again
        var afterCancel = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_LBUTTONDOWN,
            drawerDisposed: false,
            () => DrawerClickTarget.Owner,
            isPrompting: false,
            nowTickMs: 2000);
        Assert.True(afterCancel.Consume);
        Assert.True(afterCancel.ShouldTriggerOutsideClick);
    }

    [Fact]
    public void OutsideClickEvaluator_NewClickAfterClosedWithoutUp_DoesNotSwallowNewClick()
    {
        var eval = new DrawerOutsideClickEvaluator();

        var firstDown = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_RBUTTONDOWN,
            drawerDisposed: false,
            () => DrawerClickTarget.Owner,
            isPrompting: false,
            nowTickMs: 1000);
        Assert.True(firstDown.Consume);

        eval.MarkQueuedWorkCompleted();
        eval.MarkDrawerClosed();

        // Suppose user dragged outside app and released, then later clicks Owner again
        var newDown = eval.ProcessMessage(
            DrawerOutsideClickEvaluator.WM_LBUTTONDOWN,
            drawerDisposed: true,
            () => DrawerClickTarget.Owner,
            isPrompting: false,
            nowTickMs: 2000);
        Assert.False(newDown.Consume);
        Assert.True(newDown.ShouldDetach);
    }
}
