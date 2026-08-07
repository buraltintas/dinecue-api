using DineCue.Application;
using DineCue.Domain;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DineCue.Tests;

public sealed class MenuScanTests
{
    // --- Upload ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Upload_StoresImageWithoutSpendingACredit()
    {
        await using var db = TestHost.CreateDb();
        var harness = TestHost.CreateMenuScan(db);
        var (service, quota) = (harness.Service, harness.Quota);
        var userId = Guid.NewGuid();

        var response = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal("image/jpeg", response.ContentType);
        Assert.True(response.SizeBytes > 0);
        Assert.StartsWith($"menu-scans/{userId:N}/", response.ObjectName, StringComparison.Ordinal);
        Assert.Equal(0, state.UsedThisPeriod);
        Assert.Equal(1, await db.MenuScanImages.CountAsync());
    }

    [Fact]
    public async Task Upload_RejectsFileLargerThanTheConfiguredLimit()
    {
        await using var db = TestHost.CreateDb();
        var service = TestHost.CreateMenuScan(db, storage: new StorageOptions { MaxImageBytes = 64 * 1024 }).Service;
        var oversized = TestHost.JpegStream(200 * 1024);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.UploadImageAsync(Guid.NewGuid(), oversized, "image/jpeg", "menu.jpg", CancellationToken.None));

        Assert.Equal("image_too_large", error.Code);
        Assert.Equal(0, await db.MenuScanImages.CountAsync());
    }

    [Fact]
    public async Task Upload_RejectsNonImageEvenWhenTheClientClaimsItIsAnImage()
    {
        await using var db = TestHost.CreateDb();
        var service = TestHost.CreateMenuScan(db).Service;
        // A PDF renamed and declared as a JPEG: only the magic bytes decide.
        var content = new MemoryStream("%PDF-1.7 not really an image"u8.ToArray());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.UploadImageAsync(Guid.NewGuid(), content, "image/jpeg", "menu.jpg", CancellationToken.None));

        Assert.Equal("unsupported_image_type", error.Code);
    }

    [Fact]
    public void ImageContentType_DetectsSupportedFormatsFromTheirHeaderBytes()
    {
        Assert.Equal("image/jpeg", ImageContentType.Detect([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("image/png", ImageContentType.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal("image/webp", ImageContentType.Detect("RIFF1234WEBP"u8.ToArray()));
        Assert.Null(ImageContentType.Detect("GIF89a......"u8.ToArray()));
        Assert.Null(ImageContentType.Detect([0x00]));
    }

    [Fact]
    public async Task OpenImage_DoesNotServeAnotherUsersImage()
    {
        await using var db = TestHost.CreateDb();
        var service = TestHost.CreateMenuScan(db).Service;
        var owner = Guid.NewGuid();
        var upload = await service.UploadImageAsync(owner, TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);
        var accepted = await service.CreateAsync(owner, TestHost.ScanRequest(upload.ObjectName), CancellationToken.None);

        var asOwner = await service.OpenImageAsync(owner, accepted.MenuScanId, 0, CancellationToken.None);
        var asStranger = await service.OpenImageAsync(Guid.NewGuid(), accepted.MenuScanId, 0, CancellationToken.None);

        Assert.NotNull(asOwner);
        Assert.Null(asStranger);
    }

    // --- Create / queue -------------------------------------------------------------------------------

    [Fact]
    public async Task Create_QueuesTheScanAndSpendsExactlyOneCredit()
    {
        await using var db = TestHost.CreateDb();
        var queue = new RecordingMenuScanJobQueue();
        var harness = TestHost.CreateMenuScan(db, queue: queue);
        var (service, quota) = (harness.Service, harness.Quota);
        var userId = Guid.NewGuid();
        var first = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "1.jpg", CancellationToken.None);
        var second = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "2.jpg", CancellationToken.None);

        var accepted = await service.CreateAsync(userId, TestHost.ScanRequest(first.ObjectName, second.ObjectName), CancellationToken.None);
        var state = await quota.GetAsync(userId, CancellationToken.None);
        var scan = await db.MenuScans.SingleAsync();

        Assert.Equal("queued", accepted.Status);
        Assert.Equal($"/menu-scans/{accepted.MenuScanId}", accepted.StatusUrl);
        Assert.Equal(1, queue.EnqueueCount);
        // Two photos of the same menu are still one dining decision.
        Assert.Equal(1, state.UsedThisPeriod);
        Assert.Equal("queued", scan.Status);
        Assert.Equal(2, await db.MenuScanImages.CountAsync(x => x.MenuScanId == accepted.MenuScanId));
    }

    [Fact]
    public async Task Create_RejectsMoreImagesThanTheConfiguredMaximum()
    {
        await using var db = TestHost.CreateDb();
        var queue = new RecordingMenuScanJobQueue();
        var harness = TestHost.CreateMenuScan(db, queue: queue);
        var (service, quota) = (harness.Service, harness.Quota);
        var userId = Guid.NewGuid();
        var names = new List<string>();
        for (var i = 0; i < 4; i++)
            names.Add((await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", $"{i}.jpg", CancellationToken.None)).ObjectName);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateAsync(userId, TestHost.ScanRequest([.. names]), CancellationToken.None));
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal("validation_error", error.Code);
        Assert.Equal(0, queue.EnqueueCount);
        Assert.Equal(0, state.UsedThisPeriod);
    }

    [Fact]
    public async Task Create_RejectsImagesBelongingToAnotherUser()
    {
        await using var db = TestHost.CreateDb();
        var service = TestHost.CreateMenuScan(db).Service;
        var stranger = await service.UploadImageAsync(Guid.NewGuid(), TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateAsync(Guid.NewGuid(), TestHost.ScanRequest(stranger.ObjectName), CancellationToken.None));

        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task Create_RejectsArbitraryImageUrls()
    {
        await using var db = TestHost.CreateDb();
        var service = TestHost.CreateMenuScan(db).Service;
        var request = new MenuScanRequest("https://internal.example/menu.jpg", null, null, null, "en", null);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateAsync(Guid.NewGuid(), request, CancellationToken.None));

        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task Create_WhenQuotaIsExhausted_DoesNotStoreOrQueueAnything()
    {
        await using var db = TestHost.CreateDb();
        var queue = new RecordingMenuScanJobQueue();
        var harness = TestHost.CreateMenuScan(db, queue: queue);
        var (service, quota) = (harness.Service, harness.Quota);
        var userId = Guid.NewGuid();
        var upload = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);
        for (var i = 0; i < 5; i++)
            await quota.ReserveRecommendationAsync(userId, CancellationToken.None);

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.CreateAsync(userId, TestHost.ScanRequest(upload.ObjectName), CancellationToken.None));

        Assert.Equal("quota_exceeded", error.Code);
        Assert.Equal(0, queue.EnqueueCount);
        Assert.Equal(0, await db.MenuScans.CountAsync());
    }

    // --- Processing ------------------------------------------------------------------------------------

    [Fact]
    public async Task Processor_CompletesTheScanAndStoresTheTranscriptAndItems()
    {
        await using var db = TestHost.CreateDb();
        var harness = TestHost.CreateMenuScan(db, new StubMenuInterpreter());
        var (service, quota) = (harness.Service, harness.Quota);
        var userId = Guid.NewGuid();
        var upload = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);
        var accepted = await service.CreateAsync(userId, TestHost.ScanRequest(upload.ObjectName), CancellationToken.None);

        await harness.Processor.ProcessAsync(new MenuScanJob(accepted.MenuScanId, userId), CancellationToken.None);

        var response = await service.GetAsync(userId, accepted.MenuScanId, CancellationToken.None);
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal("completed", response.Status);
        Assert.Null(response.CurrentStep);
        Assert.Equal(1, response.ImageCount);
        Assert.Contains(response.Items, x => x.Name == "Lentil soup");
        Assert.Equal("Lentil soup, grilled sea bass", (await db.MenuScans.SingleAsync()).OcrText);
        Assert.Equal(1, state.UsedThisPeriod);
    }

    [Fact]
    public async Task Processor_WhenTheMenuCannotBeRead_FailsAndReturnsTheCredit()
    {
        await using var db = TestHost.CreateDb();
        var harness = TestHost.CreateMenuScan(db, new StubMenuInterpreter { Readable = false });
        var (service, quota) = (harness.Service, harness.Quota);
        var userId = Guid.NewGuid();
        var upload = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);
        var accepted = await service.CreateAsync(userId, TestHost.ScanRequest(upload.ObjectName), CancellationToken.None);

        await harness.Processor.ProcessAsync(new MenuScanJob(accepted.MenuScanId, userId), CancellationToken.None);

        var response = await service.GetAsync(userId, accepted.MenuScanId, CancellationToken.None);
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal("failed", response.Status);
        Assert.Equal("menu_not_readable", response.ErrorCode);
        // An unreadable photo is our failure, not a spent dining decision.
        Assert.Equal(0, state.UsedThisPeriod);
    }

    [Fact]
    public async Task Processor_WhenTheProviderFails_FailsAndReturnsTheCredit()
    {
        await using var db = TestHost.CreateDb();
        var harness = TestHost.CreateMenuScan(db, new ThrowingMenuInterpreter());
        var (service, quota) = (harness.Service, harness.Quota);
        var userId = Guid.NewGuid();
        var upload = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);
        var accepted = await service.CreateAsync(userId, TestHost.ScanRequest(upload.ObjectName), CancellationToken.None);

        await harness.Processor.ProcessAsync(new MenuScanJob(accepted.MenuScanId, userId), CancellationToken.None);

        var response = await service.GetAsync(userId, accepted.MenuScanId, CancellationToken.None);
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal("failed", response.Status);
        Assert.Equal("menu_provider_failed", response.ErrorCode);
        Assert.Equal(0, state.UsedThisPeriod);
    }

    [Fact]
    public async Task Processor_PassesTheStoredProfileAndTheTypedNoteToTheInterpreter()
    {
        await using var db = TestHost.CreateDb();
        var quota = TestHost.CreateQuotaService(db);
        var userId = Guid.NewGuid();
        db.TasteProfiles.Add(new TasteProfile { UserId = userId, AllergiesJson = """["peanut"]""", SpiceTolerance = 1 });
        db.DiningProfiles.Add(new DiningProfile { UserId = userId, UsuallyWithKids = true });
        await db.SaveChangesAsync();
        var interpreter = new StubMenuInterpreter();
        var harness = TestHost.CreateMenuScan(db, interpreter);
        var service = harness.Service;
        var upload = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);
        var request = TestHost.ScanRequest(upload.ObjectName) with
        {
            RawText = "çocuklar var, çok acı istemiyoruz",
            SelectedCues = ["Family"],
            Context = new Dictionary<string, object> { ["partySize"] = 4 }
        };
        var accepted = await service.CreateAsync(userId, request, CancellationToken.None);

        await harness.Processor.ProcessAsync(new MenuScanJob(accepted.MenuScanId, userId), CancellationToken.None);

        Assert.NotNull(interpreter.LastRequest);
        Assert.Equal(["peanut"], interpreter.LastRequest!.TasteProfile?.Allergies);
        Assert.True(interpreter.LastRequest.DiningProfile?.UsuallyWithKids);
        Assert.Equal("çocuklar var, çok acı istemiyoruz", interpreter.LastRequest.RawText);
        Assert.Equal(["Family"], interpreter.LastRequest.SelectedCues);
        Assert.Single(interpreter.LastRequest.ImageDataUrls);
        Assert.StartsWith("data:image/jpeg;base64,", interpreter.LastRequest.ImageDataUrls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recommend_ReRanksFromTheStoredTranscriptWithoutSpendingAnotherCredit()
    {
        await using var db = TestHost.CreateDb();
        var interpreter = new StubMenuInterpreter();
        var harness = TestHost.CreateMenuScan(db, interpreter);
        var (service, quota) = (harness.Service, harness.Quota);
        var userId = Guid.NewGuid();
        var upload = await service.UploadImageAsync(userId, TestHost.JpegStream(), "image/jpeg", "menu.jpg", CancellationToken.None);
        var accepted = await service.CreateAsync(userId, TestHost.ScanRequest(upload.ObjectName), CancellationToken.None);
        await harness.Processor.ProcessAsync(new MenuScanJob(accepted.MenuScanId, userId), CancellationToken.None);

        var response = await service.RecommendAsync(userId, accepted.MenuScanId, CancellationToken.None);
        var state = await quota.GetAsync(userId, CancellationToken.None);

        Assert.Equal("completed", response.Status);
        Assert.NotEmpty(response.Items);
        // Re-ranking reuses the transcript, so no image is read again and no credit is charged.
        Assert.Empty(interpreter.LastRequest!.ImageDataUrls);
        Assert.Equal(1, state.UsedThisPeriod);
    }

    // --- Dietary safety --------------------------------------------------------------------------------

    [Fact]
    public void MenuSafety_AlwaysAddsTheAllergenDisclaimerInTheDinersLanguage()
    {
        var interpretation = Interpretation(warnings: []);

        var english = MenuSafety.Apply(interpretation, taste: null, "en");
        var turkish = MenuSafety.Apply(interpretation, taste: null, "tr");
        var german = MenuSafety.Apply(interpretation, taste: null, "de");

        Assert.Contains(english.Warnings, x => x.Contains("confirm the ingredients with the staff", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(turkish.Warnings, x => x.Contains("personele doğrulat", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(german.Warnings, x => x.Contains("beim Personal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MenuSafety_DemotesARecommendationThatConflictsWithAStoredAllergy()
    {
        var interpretation = Interpretation();
        var taste = TestHost.Taste(allergies: ["nuts"]);

        var result = MenuSafety.Apply(interpretation, taste, "en");

        // The baklava carries a nut allergen, so it must stop being a suggestion and become a warning.
        Assert.DoesNotContain(result.Recommendations, x => x.ItemName == "Pistachio baklava");
        Assert.Contains(result.Recommendations, x => x.ItemName == "Lentil soup");
        Assert.Contains(result.Warnings, x => x.Contains("Pistachio baklava", StringComparison.Ordinal) && x.Contains("nuts", StringComparison.Ordinal));
    }

    [Fact]
    public void MenuSafety_KeepsSuggestionsWhenNothingConflicts()
    {
        var result = MenuSafety.Apply(Interpretation(), TestHost.Taste(allergies: ["shellfish"]), "en");

        Assert.Equal(2, result.Recommendations.Length);
    }

    [Fact]
    public void MenuSafety_IgnoresVeryShortProfileEntriesThatWouldMatchEverything()
    {
        // A stray one or two character entry must not silently wipe out every suggestion.
        var result = MenuSafety.Apply(Interpretation(), TestHost.Taste(allergies: ["a", "so"]), "en");

        Assert.Equal(2, result.Recommendations.Length);
    }

    private static MenuInterpretation Interpretation(string[]? warnings = null) => new(
        "en",
        "A short menu with soups, grills, and desserts.",
        [new MenuSectionDto("Mains", ["Lentil soup", "Pistachio baklava"])],
        [
            new MenuItemDto("Lentil soup", "Red lentil soup with lemon.", "Starters", "120 TRY", [], true, true, false),
            new MenuItemDto("Pistachio baklava", "Sweet pastry with pistachio.", "Desserts", "180 TRY", ["nuts", "gluten"], true, true, false)
        ],
        [
            new MenuRecommendationDto("Lentil soup", "A familiar vegetarian starter.", 0.88, []),
            new MenuRecommendationDto("Pistachio baklava", "A classic finish.", 0.8, [])
        ],
        warnings ?? ["Menu prices may have changed."],
        ["Can you confirm the allergens?"]);

    private sealed class RecordingMenuScanJobQueue : IMenuScanJobQueue
    {
        public int EnqueueCount { get; private set; }

        public ValueTask EnqueueAsync(MenuScanJob job, CancellationToken cancellationToken)
        {
            EnqueueCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<MenuScanJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubMenuInterpreter : IAiMenuInterpreter
    {
        public bool Readable { get; init; } = true;
        public MenuInterpretationRequest? LastRequest { get; private set; }

        public Task<MenuInterpretationResult> InterpretAsync(MenuInterpretationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new MenuInterpretationResult("Lentil soup, grilled sea bass", Interpretation(), Readable));
        }
    }

    private sealed class ThrowingMenuInterpreter : IAiMenuInterpreter
    {
        public Task<MenuInterpretationResult> InterpretAsync(MenuInterpretationRequest request, CancellationToken cancellationToken) =>
            throw new ProviderFailureException("menu_provider_failed", "We could not read this menu right now.");
    }
}
