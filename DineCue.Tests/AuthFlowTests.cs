using System.Net;
using DineCue.Application;
using DineCue.Domain;
using DineCue.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DineCue.Tests;

public sealed class AuthFlowTests
{
    [Fact]
    public async Task EmailOtpStart_StoresHashedOtp_SendsEmail_AndDoesNotReturnDevCodeInProduction()
    {
        await using var db = CreateDb();
        var email = new CapturingEmailSender();
        var service = CreateAuth(db, emailSender: email, environmentName: Environments.Production);

        var response = await service.StartEmailAsync(new EmailStartRequest("Person@Example.com", "TR"), CancellationToken.None);
        var otp = await db.EmailOtps.SingleAsync();

        Assert.Equal("If the email can receive a code, an OTP has been sent.", response.Message);
        Assert.Null(response.DevOtp);
        Assert.Equal("person@example.com", otp.Email);
        Assert.NotEqual(email.LastCode, otp.CodeHash);
        Assert.Equal(new TokenService(Options.Create(Jwt())).HashOtp("person@example.com", email.LastCode!), otp.CodeHash);
        Assert.Equal("tr", email.LastLocale);
    }

    [Fact]
    public async Task EmailOtpVerify_CreatesUser_AndOtpIsSingleUse()
    {
        await using var db = CreateDb();
        var email = new CapturingEmailSender();
        var service = CreateAuth(db, emailSender: email, environmentName: Environments.Development, exposeDevOtp: true);
        var start = await service.StartEmailAsync(new EmailStartRequest("new@example.com", "de"), CancellationToken.None);

        var login = await service.VerifyEmailAsync(new EmailVerifyRequest("new@example.com", start.DevOtp!, "de"), CancellationToken.None);
        var secondUse = await Assert.ThrowsAsync<ApiException>(() =>
            service.VerifyEmailAsync(new EmailVerifyRequest("new@example.com", start.DevOtp!, null), CancellationToken.None));

        Assert.True(login.IsNewUser);
        Assert.Equal("de", login.User.PreferredLanguage);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));
        Assert.Equal("invalid_otp", secondUse.Code);
    }

    [Fact]
    public async Task EmailOtpVerify_SendsWelcomeOnceAfterFirstAccountCreation()
    {
        await using var db = CreateDb();
        var email = new CapturingEmailSender();
        var service = CreateAuth(db, emailSender: email, environmentName: Environments.Development, exposeDevOtp: true);
        var start = await service.StartEmailAsync(new EmailStartRequest("welcome@example.com", "en"), CancellationToken.None);

        await service.VerifyEmailAsync(new EmailVerifyRequest("welcome@example.com", start.DevOtp!, "en"), CancellationToken.None);
        var secondStart = await service.StartEmailAsync(new EmailStartRequest("welcome@example.com", "en"), CancellationToken.None);
        await service.VerifyEmailAsync(new EmailVerifyRequest("welcome@example.com", secondStart.DevOtp!, "en"), CancellationToken.None);

        Assert.Equal(1, email.Messages.Count(x => x.Metadata?["template"] == "welcome"));
        Assert.Equal(1, await db.EmailDeliveryLedgers.CountAsync(x => x.EmailType == "welcome" && x.Status == "sent"));
    }

    [Fact]
    public async Task EmailOtpVerify_WrongCodeIncrementsAttempts_AndBlocksAfterLimit()
    {
        await using var db = CreateDb();
        var service = CreateAuth(db, emailSender: new CapturingEmailSender(), maxOtpAttempts: 1);
        await service.StartEmailAsync(new EmailStartRequest("person@example.com"), CancellationToken.None);

        var wrong = await Assert.ThrowsAsync<ApiException>(() =>
            service.VerifyEmailAsync(new EmailVerifyRequest("person@example.com", "000000"), CancellationToken.None));
        var blocked = await Assert.ThrowsAsync<ApiException>(() =>
            service.VerifyEmailAsync(new EmailVerifyRequest("person@example.com", "111111"), CancellationToken.None));

        Assert.Equal("invalid_otp", wrong.Code);
        Assert.Equal("too_many_attempts", blocked.Code);
    }

    [Fact]
    public async Task GoogleSignIn_CreatesUserAndStoresPreferredLanguage()
    {
        await using var db = CreateDb();
        var service = CreateAuth(db, google: new FixedGoogleValidator("google-sub", "google@example.com", "Dine Cue", null));

        var login = await service.GoogleAsync(new GoogleLoginRequest("valid", "tr"), CancellationToken.None);

        Assert.True(login.IsNewUser);
        Assert.Equal("tr", login.User.PreferredLanguage);
        Assert.Equal("google-sub", await db.UserIdentities.Select(x => x.ProviderUserId).SingleAsync());
    }

    [Fact]
    public async Task GoogleSignIn_SendsWelcomeOnceAfterFirstAccountCreation()
    {
        await using var db = CreateDb();
        var email = new CapturingEmailSender();
        var service = CreateAuth(db, emailSender: email, google: new FixedGoogleValidator("google-sub", "google@example.com", "Dine Cue", null));

        await service.GoogleAsync(new GoogleLoginRequest("valid", "tr"), CancellationToken.None);
        await service.GoogleAsync(new GoogleLoginRequest("valid", "tr"), CancellationToken.None);

        Assert.Equal(1, email.Messages.Count(x => x.Metadata?["template"] == "welcome"));
    }

    [Fact]
    public async Task GoogleSignIn_WelcomeProviderFailureDoesNotFailAuth()
    {
        await using var db = CreateDb();
        var email = new CapturingEmailSender(sendAsyncSucceeds: false);
        var service = CreateAuth(db, emailSender: email, google: new FixedGoogleValidator("google-sub", "google@example.com", "Dine Cue", null));

        var login = await service.GoogleAsync(new GoogleLoginRequest("valid", "en"), CancellationToken.None);

        Assert.True(login.IsNewUser);
        Assert.Equal(1, email.Messages.Count(x => x.Metadata?["template"] == "welcome"));
        Assert.Equal("failed", (await db.EmailDeliveryLedgers.SingleAsync(x => x.EmailType == "welcome")).Status);
    }


    [Fact]
    public async Task GoogleSignIn_LinksExistingEmailOtpUserWithoutOverwritingLanguage()
    {
        await using var db = CreateDb();
        var user = new User { Email = "person@example.com", PreferredLanguage = "de" };
        db.Users.Add(user);
        db.UserIdentities.Add(new UserIdentity { UserId = user.Id, Provider = "email", ProviderUserId = "person@example.com", Email = "person@example.com" });
        await db.SaveChangesAsync();
        var service = CreateAuth(db, google: new FixedGoogleValidator("google-sub", "person@example.com", "Person", null));

        var login = await service.GoogleAsync(new GoogleLoginRequest("valid", "tr"), CancellationToken.None);

        Assert.False(login.IsNewUser);
        Assert.Equal(user.Id, login.User.Id);
        Assert.Equal("de", login.User.PreferredLanguage);
        Assert.Equal(2, await db.UserIdentities.CountAsync());
    }

    [Fact]
    public async Task GoogleSignIn_ExistingProviderIdentityDoesNotCreateDuplicateUser()
    {
        await using var db = CreateDb();
        var service = CreateAuth(db, google: new FixedGoogleValidator("google-sub", "person@example.com", "Person", null));

        var first = await service.GoogleAsync(new GoogleLoginRequest("valid", "en"), CancellationToken.None);
        var second = await service.GoogleAsync(new GoogleLoginRequest("valid", "tr"), CancellationToken.None);

        Assert.True(first.IsNewUser);
        Assert.False(second.IsNewUser);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(first.User.Id, second.User.Id);
    }

    [Fact]
    public async Task GoogleSignIn_InvalidCredentialFailsSafely()
    {
        await using var db = CreateDb();
        var service = CreateAuth(db, google: new ThrowingGoogleValidator());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.GoogleAsync(new GoogleLoginRequest("bad", "en"), CancellationToken.None));

        Assert.Equal("invalid_google_credential", error.Code);
    }

    [Fact]
    public async Task GoogleAuthValidator_InvalidTokenFailsSafely()
    {
        var validator = new GoogleAuthValidator(
            Options.Create(new GoogleAuthOptions { Enabled = true, ClientId = "client-1", AllowedAudiences = ["client-1"], TimeoutSeconds = 1 }),
            NullLogger<GoogleAuthValidator>.Instance);

        var error = await Assert.ThrowsAsync<ApiException>(() => validator.ValidateAsync("not-a-google-id-token", CancellationToken.None));

        Assert.Equal("invalid_google_credential", error.Code);
    }

    private static AuthService CreateAuth(
        DineCueDbContext db,
        IEmailSender? emailSender = null,
        IGoogleAuthValidator? google = null,
        string environmentName = "Production",
        bool exposeDevOtp = false,
        int maxOtpAttempts = 5)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var abuse = new AbuseProtectionService(
            cache,
            Options.Create(new AbuseProtectionOptions
            {
                AccountCreationLimitPerEmailWindow = 10,
                AccountCreationLimitPerProviderWindow = 10,
                AccountCreationWindowMinutes = 60
            }),
            Options.Create(Jwt()));
        return new AuthService(
            db,
            new TokenService(Options.Create(Jwt())),
            google ?? new FixedGoogleValidator("google-sub", "person@example.com", "Person", null),
            emailSender ?? new CapturingEmailSender(),
            new OtpRateLimiter(cache, Options.Create(new EmailOtpOptions { MaxAttempts = maxOtpAttempts, ExposeDevOtp = exposeDevOtp, StartLimitPerEmailWindow = 10, VerifyLimitPerEmailWindow = 10, EmailWindowMinutes = 15 }), abuse),
            abuse,
            new EmailNotificationService(
                db,
                emailSender ?? new CapturingEmailSender(),
                new EmailTemplateRenderer(),
                Options.Create(new EmailOptions { AppBaseUrl = "https://app.example.com" }),
                Options.Create(new ProductEmailOptions { WelcomeEnabled = true }),
                NullLogger<EmailNotificationService>.Instance),
            new TestEnvironment(environmentName),
            Options.Create(new EmailOtpOptions { ExpiryMinutes = 5, MaxAttempts = maxOtpAttempts, ExposeDevOtp = exposeDevOtp }),
            Options.Create(Jwt()),
            NullLogger<AuthService>.Instance);
    }

    private static DineCueDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DineCueDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static JwtOptions Jwt() => new()
    {
        Issuer = "DineCue",
        Audience = "DineCue.Mobile",
        SigningKey = "test-signing-key-at-least-32-characters-long",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 60
    };

    private sealed class CapturingEmailSender(bool sendAsyncSucceeds = true) : IEmailSender
    {
        public string? LastCode { get; private set; }
        public string? LastLocale { get; private set; }
        public List<EmailMessage> Messages { get; } = [];
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            Capture(message);

        public Task<EmailSendResult> SendOtpAsync(string email, string code, string? locale, CancellationToken cancellationToken)
        {
            LastCode = code;
            LastLocale = locale;
            return Capture(new EmailMessage(email, "OTP", "", "", locale ?? "en", new Dictionary<string, string> { ["template"] = "email_verification" }));
        }

        private Task<EmailSendResult> Capture(EmailMessage message)
        {
            Messages.Add(message);
            return Task.FromResult(sendAsyncSucceeds ? new EmailSendResult(true, "test-email") : new EmailSendResult(false, ErrorCode: "provider_failed"));
        }
    }

    private sealed class FixedGoogleValidator(string subject, string email, string? name, string? avatar) : IGoogleAuthValidator
    {
        public Task<GoogleUserInfo> ValidateAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(new GoogleUserInfo(subject, email, name, avatar));
    }

    private sealed class ThrowingGoogleValidator : IGoogleAuthValidator
    {
        public Task<GoogleUserInfo> ValidateAsync(string token, CancellationToken cancellationToken) =>
            throw new ApiException("invalid_google_credential", "Google sign-in could not be completed.", 401);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DineCue.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
