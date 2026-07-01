using DineCue.Application;
using DineCue.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    public void AuthContracts_RemainCompatible()
    {
        var request = new GoogleLoginRequest("credential", "tr");
        var properties = typeof(GoogleLoginRequest).GetProperties().Select(x => x.Name).ToArray();

        Assert.Equal("credential", request.Token);
        Assert.Equal("tr", request.PreferredLanguage);
        Assert.Contains("Token", properties);
        Assert.Contains("PreferredLanguage", properties);
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

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DineCue.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
