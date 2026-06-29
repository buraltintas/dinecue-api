using System.Net;
using System.Text;
using DineCue.Application;
using DineCue.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DineCue.Tests;

public sealed class EmailInfrastructureTests
{
    [Fact]
    public void TemplateRenderer_FallsBackToEnglish_WhenLocaleIsUnsupported()
    {
        var renderer = new EmailTemplateRenderer();

        var rendered = renderer.RenderEmailVerification(new EmailTemplateModel("fr", Code: "123456", ExpiresInMinutes: 5));

        Assert.Equal("Your sign-in code", rendered.Subject);
        Assert.Contains("123456", rendered.TextBody);
    }

    [Fact]
    public void TemplateRenderer_RendersLocalizedVerificationEmail()
    {
        var renderer = new EmailTemplateRenderer();

        var rendered = renderer.RenderEmailVerification(new EmailTemplateModel("tr", Code: "123456", ExpiresInMinutes: 5));

        Assert.Equal("Giriş kodun", rendered.Subject);
        Assert.Contains("123456", rendered.TextBody);
        Assert.Contains("DineCue", rendered.HtmlBody);
    }

    [Fact]
    public void TemplateRenderer_RendersContactFeedbackNotification()
    {
        var renderer = new EmailTemplateRenderer();

        var rendered = renderer.RenderContactFeedbackNotification(new EmailTemplateModel("de", SenderEmail: "guest@example.com", Message: "Danke für DineCue."));

        Assert.Equal("Neue Nachricht für DineCue", rendered.Subject);
        Assert.Contains("guest@example.com", rendered.TextBody);
        Assert.Contains("Danke für DineCue.", rendered.TextBody);
    }

    [Fact]
    public async Task DevelopmentEmailSender_ReturnsSuccessWithoutSending()
    {
        var sender = new DevelopmentEmailSender(
            new EmailTemplateRenderer(),
            Options.Create(new EmailOtpOptions { ExpiryMinutes = 5 }),
            NullLogger<DevelopmentEmailSender>.Instance);

        var result = await sender.SendOtpAsync("person@example.com", "123456", "en", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("email_disabled", result.ErrorCode);
    }

    [Fact]
    public async Task ResendEmailSender_ReturnsFailureForProviderErrors()
    {
        var http = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)))
        {
            BaseAddress = new Uri("https://api.resend.com")
        };
        var sender = new ResendEmailSender(
            http,
            Options.Create(new EmailOptions
            {
                Enabled = true,
                Provider = "resend",
                FromEmail = "hello@example.com",
                FromName = "DineCue",
                ResendApiKey = "placeholder",
                TimeoutSeconds = 10
            }),
            Options.Create(new EmailOtpOptions { ExpiryMinutes = 5 }),
            new EmailTemplateRenderer(),
            NullLogger<ResendEmailSender>.Instance);

        var result = await sender.SendOtpAsync("person@example.com", "123456", "en", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("provider_failed", result.ErrorCode);
    }

    [Fact]
    public void DependencyInjection_ResolvesEmailSender()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=dinecue;Username=dinecue;Password=placeholder",
            ["Jwt:Issuer"] = "DineCue",
            ["Jwt:Audience"] = "DineCue.Mobile",
            ["Jwt:SigningKey"] = "replace-with-a-long-random-development-secret",
            ["Email:Enabled"] = "false",
            ["Recommendation:UseMockProvider"] = "true"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDineCueInfrastructure(configuration, new TestEnvironment());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DevelopmentEmailSender>(provider.GetRequiredService<IEmailSender>());
        Assert.NotNull(provider.GetRequiredService<IEmailTemplateRenderer>());
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            response.Content ??= new StringContent("{}", Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "DineCue.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
