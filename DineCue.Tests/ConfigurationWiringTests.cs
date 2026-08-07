using DineCue.Application;
using DineCue.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Xunit;

namespace DineCue.Tests;

public sealed class ConfigurationWiringTests
{
    [Fact]
    public void EmailOptions_BindsReplyToAndBrandingUrls()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "resend",
                ["Email:FromEmail"] = "hello@dinecue.com",
                ["Email:FromName"] = "DineCue",
                ["Email:ReplyToEmail"] = "info@coffeedictionary.com",
                ["Email:BrandLogoUrl"] = "https://dinecue.com/dinecue-logo.png",
                ["Email:BrandIconUrl"] = "https://dinecue.com/dinecue-icon.png",
                ["Email:PrivacyUrl"] = "https://dinecue.com/privacy",
                ["Email:TermsUrl"] = "https://dinecue.com/terms"
            })
            .Build();

        var options = config.GetSection("Email").Get<EmailOptions>();

        Assert.NotNull(options);
        Assert.Equal("info@coffeedictionary.com", options.ReplyToEmail);
        Assert.Equal("https://dinecue.com/dinecue-logo.png", options.BrandLogoUrl);
        Assert.Equal("https://dinecue.com/dinecue-icon.png", options.BrandIconUrl);
        Assert.Equal("https://dinecue.com/privacy", options.PrivacyUrl);
        Assert.Equal("https://dinecue.com/terms", options.TermsUrl);
    }

    [Fact]
    public void GoogleAuthOptions_BindsClientIdAndAllowedAudiences()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleAuth:Enabled"] = "true",
                ["GoogleAuth:ClientId"] = "client-1",
                ["GoogleAuth:AllowedAudiences:0"] = "client-1",
                ["GoogleAuth:AllowedAudiences:1"] = "client-2",
                ["GoogleAuth:TimeoutSeconds"] = "7"
            })
            .Build();

        var options = config.GetSection("GoogleAuth").Get<GoogleAuthOptions>();

        Assert.NotNull(options);
        Assert.True(options.Enabled);
        Assert.Equal("client-1", options.ClientId);
        Assert.Equal(["client-1", "client-2"], options.AllowedAudiences);
        Assert.Equal(["client-1", "client-2"], options.EffectiveAllowedAudiences());
        Assert.Equal(7, options.TimeoutSeconds);
    }

    [Fact]
    public void GoogleAuthOptions_DefaultsAllowedAudienceToClientId()
    {
        var options = new GoogleAuthOptions { Enabled = true, ClientId = "client-1" };

        Assert.Equal(["client-1"], options.EffectiveAllowedAudiences());
    }

    [Fact]
    public void GoogleAuthConfigValidation_FailsWhenEnabledWithoutClientId()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BaseConfig(new Dictionary<string, string?>
        {
            ["GoogleAuth:Enabled"] = "true",
            ["GoogleAuth:ClientId"] = ""
        });

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddDineCueInfrastructure(config, new TestEnvironment(Environments.Production)));

        Assert.Contains("GoogleAuth:ClientId", error.Message);
    }

    [Fact]
    public void DependencyInjection_RegistersRealGoogleValidatorWhenEnabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BaseConfig(new Dictionary<string, string?>
        {
            ["GoogleAuth:Enabled"] = "true",
            ["GoogleAuth:ClientId"] = "client-1",
            ["GoogleAuth:AllowedAudiences:0"] = "client-1"
        });

        services.AddDineCueInfrastructure(config, new TestEnvironment(Environments.Production));
        using var provider = services.BuildServiceProvider();

        var validator = provider.GetRequiredService<IGoogleAuthValidator>();
        Assert.IsType<GoogleAuthValidator>(validator);
        Assert.DoesNotContain("MockGoogleAuthValidator", validator.GetType().Name);
    }

    [Fact]
    public void DependencyInjection_DoesNotUseMockGoogleAuthInProductionWhenDisabled()
    {
        var services = new ServiceCollection();
        var config = BaseConfig(new Dictionary<string, string?>
        {
            ["GoogleAuth:Enabled"] = "false"
        });

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddDineCueInfrastructure(config, new TestEnvironment(Environments.Production)));

        Assert.Contains("GoogleAuth:Enabled", error.Message);
    }

    [Fact]
    public void StorageOptions_BindAndClampToSafeValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Enabled"] = "true",
                ["Storage:Bucket"] = "dinecue-menu-images",
                ["Storage:ObjectPrefix"] = "menu-scans",
                ["Storage:MaxImagesPerScan"] = "99",
                ["Storage:MaxImageBytes"] = "1",
                ["Storage:AllowedContentTypes:0"] = "image/jpeg"
            })
            .Build();

        var options = config.GetSection("Storage").Get<StorageOptions>();

        Assert.NotNull(options);
        Assert.True(options.Enabled);
        Assert.Equal("dinecue-menu-images", options.Bucket);
        // Configuring the list narrows it; it must not be appended to the built-in defaults.
        Assert.Equal(["image/jpeg"], options.EffectiveAllowedContentTypes());
        Assert.Equal(
            ["image/jpeg", "image/png", "image/webp"],
            new StorageOptions().EffectiveAllowedContentTypes());
        // Misconfiguration must not turn into an unbounded vision bill or a useless byte limit.
        Assert.Equal(6, options.EffectiveMaxImagesPerScan());
        Assert.Equal(64 * 1024, options.EffectiveMaxImageBytes());
    }

    [Fact]
    public void StorageConfigValidation_FailsWhenEnabledWithoutABucket()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BaseConfig(new Dictionary<string, string?>
        {
            ["GoogleAuth:Enabled"] = "true",
            ["GoogleAuth:ClientId"] = "client-1",
            ["Storage:Enabled"] = "true",
            ["Storage:Bucket"] = ""
        });

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.AddDineCueInfrastructure(config, new TestEnvironment(Environments.Production)));

        Assert.Contains("Storage:Bucket", error.Message);
    }

    [Fact]
    public void DependencyInjection_UsesInProcessStorageWhenStorageIsDisabled()
    {
        // Menu scanning must degrade to a local no-op rather than break the recommendation flow.
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BaseConfig(new Dictionary<string, string?>
        {
            ["GoogleAuth:Enabled"] = "true",
            ["GoogleAuth:ClientId"] = "client-1",
            ["Storage:Enabled"] = "false"
        });

        services.AddDineCueInfrastructure(config, new TestEnvironment(Environments.Production));
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryObjectStorage>(provider.GetRequiredService<IObjectStorage>());
    }

    [Fact]
    public void OpenAIOptions_FallBackToTheMainModelForVisionCalls()
    {
        Assert.Equal("gpt-4.1-mini", new OpenAIOptions { Model = "gpt-4.1-mini" }.EffectiveVisionModel());
        Assert.Equal("gpt-4.1", new OpenAIOptions { Model = "gpt-4.1-mini", VisionModel = "gpt-4.1" }.EffectiveVisionModel());
    }

    [Fact]
    public void AuthContracts_RemainCompatible()
    {
        var request = new GoogleLoginRequest("credential", "tr");
        var properties = typeof(GoogleLoginRequest).GetProperties().Select(x => x.Name).ToArray();

        Assert.Equal("credential", request.Token);
        Assert.Equal("tr", request.PreferredLanguage);
        Assert.Contains("Token", properties);
        Assert.Contains("PreferredLanguage", properties);
    }

    [Fact]
    public void RecommendationLocationValidation_AcceptsSupportedModes()
    {
        var cases = new[]
        {
            new LocationInput("text", "Selanik", null, null, null),
            new LocationInput("current", null, 40.9909, 29.0303, null),
            new LocationInput("map_pin", "Selanik", 40.6401, 22.9444, null),
            new LocationInput("place", "Selanik", null, null, "google-place-id")
        };

        foreach (var location in cases)
        {
            RequestValidation.Recommendation(new RecommendationSessionRequest("Dinner", location, [], "en", null));
        }
    }

    /// <summary>
    /// Pins the fields the dinecue-ui reads. Adding fields is fine (TypeScript ignores extras), but renaming
    /// or retyping one of these silently breaks the live UI, so this test fails first instead.
    /// </summary>
    [Theory]
    [InlineData(typeof(RecommendationCardDto), "Id:Guid,Rank:Int32,Title:String,Headline:String,PlaceName:String,Address:String,Vibe:String,Summary:String,WhyThisPlace:String,WhatToOrder:String[],GoodToKnow:String,Cautions:String[],Confidence:Double,Reservation:ReservationDto,RouteUrl:String,ShareText:String")]
    [InlineData(typeof(ReservationDto), "Status:String,Provider:String,Url:String,PhoneNumber:String,Confidence:Double")]
    [InlineData(typeof(RecommendationSessionDetailResponse), "SessionId:Guid,Status:String,CurrentStep:String,RawText:String,Language:String,LocationMode:String,LocationText:String,CreatedAt:DateTimeOffset,StartedAt:Nullable`1,CompletedAt:Nullable`1,FailedAt:Nullable`1,ErrorCode:String,ErrorMessage:String,NormalizedContext:Dictionary`2,Assumptions:Dictionary`2,Recommendations:IReadOnlyList`1")]
    [InlineData(typeof(RecommendationSessionAcceptedResponse), "SessionId:Guid,Status:String,StatusUrl:String")]
    [InlineData(typeof(SavedPlaceDto), "Id:Guid,Provider:String,ProviderPlaceId:String,RecommendationResultId:Nullable`1,Name:String,Address:String,Note:String,CreatedAt:DateTimeOffset")]
    [InlineData(typeof(HistoryItemDto), "SessionId:Guid,RawText:String,LocationMode:String,LocationText:String,CreatedAt:DateTimeOffset,Results:IReadOnlyList`1")]
    [InlineData(typeof(ProfileQuotaDto), "Plan:String,MonthlyLimit:Int32,UsedThisPeriod:Int32,RemainingThisPeriod:Int32,PeriodKey:String,PeriodEndsAt:DateTimeOffset,ProAvailable:Boolean,ProStatus:String")]
    [InlineData(typeof(QuotaStateResponse), "Plan:String,MonthlyLimit:Int32,UsedThisPeriod:Int32,RemainingThisPeriod:Int32,PeriodKey:String,PeriodEndsAt:DateTimeOffset,ProAvailable:Boolean,ProStatus:String")]
    public void UiFacingContracts_KeepTheirExistingFieldNamesAndTypes(Type contract, string requiredMembers)
    {
        var actual = contract.GetProperties().ToDictionary(x => x.Name, x => x.PropertyType.Name, StringComparer.Ordinal);

        foreach (var member in requiredMembers.Split(','))
        {
            var parts = member.Split(':');
            Assert.True(actual.ContainsKey(parts[0]), $"{contract.Name}.{parts[0]} is missing; the UI reads it.");
            Assert.Equal(parts[1], actual[parts[0]]);
        }
    }

    [Fact]
    public void RecommendationCard_ExposesTheProviderPlaceIdNeededToStartAFitCheck()
    {
        // Fit check is addressed by Google place id, and the card is where the UI starts that flow.
        var properties = typeof(RecommendationCardDto).GetProperties().Select(x => x.Name).ToArray();

        Assert.Contains("ProviderPlaceId", properties);
    }

    [Fact]
    public void ApiRateLimiterPolicies_AreRegisteredForEveryEndpointPolicy()
    {
        var repoRoot = FindRepoRoot();
        var apiDir = Path.Combine(repoRoot, "DineCue.Api");
        var program = File.ReadAllText(Path.Combine(apiDir, "Program.cs"));
        var registered = Regex.Matches(program, "AddPolicy\\(\"([^\"]+)\"")
            .Select(x => x.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var used = Directory.EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "EnableRateLimiting\\(\"([^\"]+)\""))
            .Select(x => x.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("recommendations", registered);
        Assert.Empty(used.Except(registered));
    }

    private static IConfiguration BaseConfig(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=dinecue;Username=dinecue;Password=placeholder",
            ["Jwt:Issuer"] = "DineCue",
            ["Jwt:Audience"] = "DineCue.Mobile",
            ["Jwt:SigningKey"] = "test-signing-key-at-least-32-characters-long",
            ["Recommendation:UseMockProvider"] = "true",
            ["Email:Enabled"] = "false"
        };
        foreach (var pair in overrides)
            values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "DineCue.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DineCue.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
