using DineCue.Application;
using DineCue.Domain;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DineCue.Tests;

/// <summary>
/// Shared construction helpers for the infrastructure services. Follows the existing test style: real
/// services wired against EF InMemory plus small hand-written fakes, no mocking library.
/// </summary>
internal static class TestHost
{
    public const string SigningKey = "test-signing-key-at-least-32-characters-long";

    public static DineCueDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DineCueDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    public static IQuotaService CreateQuotaService(DineCueDbContext db) =>
        new QuotaService(
            db,
            new MockSubscriptionProvider(db),
            Options.Create(new QuotasOptions()),
            Options.Create(new JwtOptions { SigningKey = SigningKey }));

    /// <summary>
    /// The upload endpoint, the scan service, and the worker must all see the same object storage and the
    /// same quota ledger, so they are built together rather than one at a time.
    /// </summary>
    public sealed record MenuScanHarness(
        MenuScanService Service,
        MenuScanProcessor Processor,
        IQuotaService Quota,
        IObjectStorage Storage);

    public static MenuScanHarness CreateMenuScan(
        DineCueDbContext db,
        IAiMenuInterpreter? interpreter = null,
        IMenuScanJobQueue? queue = null,
        StorageOptions? storage = null)
    {
        var options = Options.Create(storage ?? new StorageOptions());
        var objectStorage = new InMemoryObjectStorage();
        var quota = CreateQuotaService(db);
        interpreter ??= new MockAiMenuInterpreter();
        var processor = CreateMenuScanProcessor(db, quota, interpreter, objectStorage, options);
        var service = new MenuScanService(
            db,
            objectStorage,
            interpreter,
            queue ?? new NoopMenuScanJobQueue(),
            quota,
            processor,
            options,
            NullLogger<MenuScanService>.Instance);
        return new MenuScanHarness(service, processor, quota, objectStorage);
    }

    public static MenuScanProcessor CreateMenuScanProcessor(
        DineCueDbContext db,
        IQuotaService quota,
        IAiMenuInterpreter interpreter,
        IObjectStorage? objectStorage = null,
        IOptions<StorageOptions>? storage = null) =>
        new(db,
            objectStorage ?? new InMemoryObjectStorage(),
            interpreter,
            quota,
            new NoopInteractionEventService(),
            storage ?? Options.Create(new StorageOptions()),
            NullLogger<MenuScanProcessor>.Instance);

    public static RestaurantService CreateRestaurantService(
        DineCueDbContext db,
        IPlacesProvider places,
        IAiRestaurantFitAnalyzer fit,
        IQuotaService? quota = null) =>
        new(db,
            places,
            new MockLocationResolver(),
            new PlacesReservationLinkResolver(),
            fit,
            new NoopInteractionEventService(),
            quota ?? CreateQuotaService(db));

    /// <summary>A minimal but genuinely JPEG-headed byte stream, so magic-byte validation passes.</summary>
    public static MemoryStream JpegStream(int size = 1024)
    {
        var bytes = new byte[Math.Max(4, size)];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        return new MemoryStream(bytes);
    }

    public static MenuScanRequest ScanRequest(params string[] objectNames) =>
        new(null, null, null, null, "en", null, ImageObjectNames: objectNames);

    public static TasteProfileDto Taste(string[]? allergies = null, string[]? dietaryRestrictions = null, string[]? dislikedIngredients = null) =>
        new([], [], [], dislikedIngredients ?? [], 2, "balanced", [], dietaryRestrictions ?? [], allergies ?? []);

    public static PlaceCandidate Candidate(string placeId = "place-1", string name = "Harbor Meze House", string metadataJson = "{}") =>
        new("google_places", placeId, name, "Iskele Cad. No:4, Antalya", 36.8862, 30.7048, 4.5m, 531, 2, metadataJson);

    public static DiningIntent Intent(
        string rawText = "Dinner with kids",
        string language = "en",
        string[]? cues = null,
        LocationInput? location = null,
        DiningProfileDto? diningProfile = null) =>
        new(Guid.NewGuid(),
            rawText,
            language,
            cues ?? [],
            new Dictionary<string, object>(),
            location ?? new LocationInput("text", "Antalya", null, null, null),
            null,
            null,
            diningProfile,
            [],
            []);

    public sealed class NoopMenuScanJobQueue : IMenuScanJobQueue
    {
        public ValueTask EnqueueAsync(MenuScanJob job, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<MenuScanJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    public sealed class NoopInteractionEventService : IInteractionEventService
    {
        public Task TrackAsync(Guid userId, InteractionEventRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Serves canned HTTP responses in order and records what was sent. Responses are produced by factories
    /// because callers dispose each response, so the same instance cannot be handed out twice.
    /// </summary>
    public sealed class StubHttpHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int index;

        public int RequestCount { get; private set; }
        public List<string> RequestBodies { get; } = [];
        public List<string?> FieldMasks { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            FieldMasks.Add(request.Headers.TryGetValues("X-Goog-FieldMask", out var masks) ? string.Join(",", masks) : null);
            RequestBodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            var factory = responses[Math.Min(index, responses.Length - 1)];
            index++;
            return factory();
        }

        public static Func<HttpResponseMessage> Json(string body) =>
            () => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };

        public static Func<HttpResponseMessage> Status(System.Net.HttpStatusCode status) =>
            () => new HttpResponseMessage(status);

        /// <summary>Wraps a JSON object as an OpenAI chat-completions response.</summary>
        public static Func<HttpResponseMessage> OpenAi(string contentJson)
        {
            var escaped = System.Text.Json.JsonSerializer.Serialize(contentJson);
            return Json("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":" + escaped + "}}]}");
        }
    }

    public static OpenAiJsonClient CreateOpenAiClient(HttpMessageHandler handler, OpenAIOptions? options = null) =>
        new(new HttpClient(handler),
            Options.Create(options ?? new OpenAIOptions { ApiKey = "key", Model = "gpt-test" }),
            NullLogger<OpenAiJsonClient>.Instance);

    public static IMemoryCache Cache() => new MemoryCache(new MemoryCacheOptions());
}
