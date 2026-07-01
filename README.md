# DineCue API

DineCue API is the backend for the DineCue mobile experience. It handles authentication, profiles, restaurant/recommendation workflows, async recommendation generation, and realtime status notifications.

## Tech Stack

- ASP.NET Core
- PostgreSQL
- Entity Framework Core
- JWT authentication
- SignalR
- Resend transactional email
- Google Places API New
- OpenAI API

## Local Prerequisites

- .NET SDK compatible with the solution target framework
- Docker Desktop or a local PostgreSQL instance
- Google Cloud project with Places API New enabled
- OpenAI API access
- Resend account for production email delivery

## Configuration And Secrets

Do not commit real secrets. Configure local secrets with `dotnet user-secrets`, environment variables, or a cloud secret manager.

Required configuration keys:

- `ConnectionStrings:DefaultConnection`
- `Jwt:SigningKey`
- `Jwt:Issuer`
- `Jwt:Audience`
- `GooglePlaces:ApiKey`
- `OpenAI:ApiKey`
- `OpenAI:Model`
- `GooglePlaces:RequestTimeoutSeconds`
- `OpenAI:RequestTimeoutSeconds`
- `Quotas:MonthlyFree`
- `Quotas:MonthlyPro`
- `Email:Provider`
- `Email:FromEmail`
- `Email:FromName`
- `Email:ReplyToEmail`
- `Email:BrandLogoUrl`
- `Email:BrandIconUrl`
- `Email:PrivacyUrl`
- `Email:TermsUrl`
- `Email:ResendApiKey`
- `Email:AppBaseUrl`
- `Email:Enabled`
- `Email:TimeoutSeconds`
- `ProductEmail:WelcomeEnabled`
- `ProductEmail:MonthlyRecapEnabled`
- `ProductEmail:MonthlyRecapEnabledByDefault`
- `ProductEmail:HistoryUrlPath`
- `GoogleAuth:Enabled`
- `GoogleAuth:ClientId`
- `GoogleAuth:AllowedAudiences`
- `GoogleAuth:TimeoutSeconds`
- `AbuseProtection:DisposableEmailDomains`
- `AbuseProtection:AccountCreationLimitPerEmailWindow`
- `AbuseProtection:AccountCreationLimitPerProviderWindow`
- `AbuseProtection:AccountCreationWindowMinutes`

Local user-secrets example:

```bash
dotnet user-secrets init --project DineCue.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<postgres-connection-string>" --project DineCue.Api
dotnet user-secrets set "Jwt:SigningKey" "<long-random-secret-at-least-32-characters>" --project DineCue.Api
dotnet user-secrets set "Jwt:Issuer" "DineCue" --project DineCue.Api
dotnet user-secrets set "Jwt:Audience" "DineCue.Mobile" --project DineCue.Api
dotnet user-secrets set "GooglePlaces:ApiKey" "<google-places-api-key>" --project DineCue.Api
dotnet user-secrets set "OpenAI:ApiKey" "<openai-api-key>" --project DineCue.Api
dotnet user-secrets set "OpenAI:Model" "<openai-model>" --project DineCue.Api
dotnet user-secrets set "GooglePlaces:RequestTimeoutSeconds" "12" --project DineCue.Api
dotnet user-secrets set "OpenAI:RequestTimeoutSeconds" "30" --project DineCue.Api
dotnet user-secrets set "Quotas:MonthlyFree" "5" --project DineCue.Api
dotnet user-secrets set "Quotas:MonthlyPro" "50" --project DineCue.Api
dotnet user-secrets set "Email:Provider" "resend" --project DineCue.Api
dotnet user-secrets set "Email:FromEmail" "<verified-sender-email>" --project DineCue.Api
dotnet user-secrets set "Email:FromName" "DineCue" --project DineCue.Api
dotnet user-secrets set "Email:ReplyToEmail" "<support-email>" --project DineCue.Api
dotnet user-secrets set "Email:BrandLogoUrl" "https://dinecue.com/dinecue-logo.png" --project DineCue.Api
dotnet user-secrets set "Email:BrandIconUrl" "https://dinecue.com/dinecue-icon.png" --project DineCue.Api
dotnet user-secrets set "Email:PrivacyUrl" "https://dinecue.com/privacy" --project DineCue.Api
dotnet user-secrets set "Email:TermsUrl" "https://dinecue.com/terms" --project DineCue.Api
dotnet user-secrets set "Email:ResendApiKey" "<resend-api-key>" --project DineCue.Api
dotnet user-secrets set "Email:AppBaseUrl" "<app-base-url>" --project DineCue.Api
dotnet user-secrets set "Email:Enabled" "false" --project DineCue.Api
dotnet user-secrets set "Email:TimeoutSeconds" "10" --project DineCue.Api
dotnet user-secrets set "ProductEmail:WelcomeEnabled" "true" --project DineCue.Api
dotnet user-secrets set "ProductEmail:MonthlyRecapEnabled" "false" --project DineCue.Api
dotnet user-secrets set "ProductEmail:MonthlyRecapEnabledByDefault" "false" --project DineCue.Api
dotnet user-secrets set "ProductEmail:HistoryUrlPath" "/app/history" --project DineCue.Api
dotnet user-secrets set "GoogleAuth:Enabled" "true" --project DineCue.Api
dotnet user-secrets set "GoogleAuth:ClientId" "<google-web-client-id>" --project DineCue.Api
dotnet user-secrets set "GoogleAuth:AllowedAudiences:0" "<google-web-client-id>" --project DineCue.Api
dotnet user-secrets set "GoogleAuth:TimeoutSeconds" "10" --project DineCue.Api
dotnet user-secrets set "AbuseProtection:AccountCreationLimitPerEmailWindow" "3" --project DineCue.Api
dotnet user-secrets set "AbuseProtection:AccountCreationLimitPerProviderWindow" "3" --project DineCue.Api
dotnet user-secrets set "AbuseProtection:AccountCreationWindowMinutes" "60" --project DineCue.Api
```

For local mock recommendation development, set:

```bash
dotnet user-secrets set "Recommendation:UseMockProvider" "true" --project DineCue.Api
```

For real Google Places + OpenAI recommendations, set:

```bash
dotnet user-secrets set "Recommendation:UseMockProvider" "false" --project DineCue.Api
```

## Local Database

With Docker Compose, set `POSTGRES_PASSWORD` in your local shell or a local `.env` file that is not committed, then run:

```bash
docker compose up -d
```

Run migrations:

```bash
dotnet ef database update --project DineCue.Infrastructure --startup-project DineCue.Api
```

## Run The API

```bash
dotnet run --project DineCue.Api
```

Swagger is available in development when enabled:

```text
http://localhost:5000/swagger
```

## Auth Model

DineCue supports Google Sign-In and email OTP sign-in only. There is no password login, no classic email/password registration, no forgot password flow, and no password reset flow.

The browser talks to the Next.js BFF, and the BFF talks to this API. The API returns access and refresh tokens to the BFF so they can be stored in httpOnly cookies by the BFF layer. Tokens, provider credentials, OTP hashes, refresh token hashes, and provider keys must never be exposed to browser-visible JavaScript.

Email OTP behavior:

- `POST /auth/email/start` normalizes the email, rate-limits repeated requests, stores only a hashed OTP, invalidates earlier active OTPs, and sends the code by email.
- The start response is generic and does not reveal whether an account exists.
- Development OTP exposure is only available when the API is running in Development and `EmailOtp:ExposeDevOtp` is enabled.
- `POST /auth/email/verify` checks the hash, expiry, attempt limit, and single-use status before creating or signing in the user.

Google Sign-In behavior:

- `POST /auth/google` accepts the Google credential from the BFF.
- The backend verifies it server-side using Google token verification and `GoogleAuth:ClientId`.
- The API validates audience, issuer, expiration, and verified email before using the stable Google subject.
- Existing Google provider identities sign in the same user.
- A verified Google email can link to an existing email OTP user.
- New users are created only after verification succeeds.

Preferred language can be sent during first sign-in. New users store valid `en`, `tr`, or `de`; existing users keep their current language unless changed through the profile update flow.

## Async Recommendation Flow

- `POST /recommendation-sessions` authenticates the user, validates the request, creates a session, queues a background job, and returns `202 Accepted`.
- The background worker calls backend-only Google Places and OpenAI providers, then persists recommendations.
- `GET /recommendation-sessions/{id}` is the source of truth for status, errors, and results.
- SignalR at `/hubs/recommendations` only sends status notifications. It does not send full recommendation data.

## Monthly Quotas

DineCue uses backend-enforced monthly usage credits. The default free plan has 5 credits per calendar month and the future/manual Pro plan has 50 credits per calendar month. Public upgrade, checkout, Stripe, and billing flows are not implemented yet.

Quota-consuming actions share one monthly pool:

- Recommendation sessions
- Menu scans
- Restaurant fit checks

The client can read safe quota state with:

```text
GET /quota
```

The response includes the current plan, monthly limit, used credits, remaining credits, period key, and period end. The client cannot set plan or quota values. Provider failures during recommendation generation release the reserved recommendation credit, so failed Google Places/OpenAI jobs do not consume the user's monthly allowance.

Free-plan usage is also associated with privacy-conscious identity hashes such as normalized email and OAuth provider subject. This helps prevent deleted or recreated accounts from receiving a fresh Free allowance in the same month without storing raw abuse-tracking identity data.

Basic abuse protection is configurable:

- OTP starts and verification attempts are rate limited.
- First-account creation is throttled per normalized email hash and provider identity hash.
- Disposable email domains can be added to `AbuseProtection:DisposableEmailDomains`.
- User-facing responses stay generic and do not expose whether an account exists or which internal rule applied.

## Email Delivery

DineCue uses backend-only transactional email delivery. The first production provider is Resend.

Supported email template scope:

- Welcome email
- Existing email verification code emails
- Monthly dining recap emails for users with real recommendation activity
- Account notification emails when supported by an existing backend flow
- Contact or feedback form notification emails

Local development mode:

```bash
dotnet user-secrets set "Email:Enabled" "false" --project DineCue.Api
```

When email delivery is disabled, the backend suppresses real delivery and logs only safe delivery metadata. It does not log OTP codes.

Production Resend setup:

```bash
dotnet user-secrets set "Email:Provider" "resend" --project DineCue.Api
dotnet user-secrets set "Email:Enabled" "true" --project DineCue.Api
dotnet user-secrets set "Email:FromEmail" "<verified-sender-email>" --project DineCue.Api
dotnet user-secrets set "Email:FromName" "DineCue" --project DineCue.Api
dotnet user-secrets set "Email:ReplyToEmail" "<support-email>" --project DineCue.Api
dotnet user-secrets set "Email:BrandLogoUrl" "https://dinecue.com/dinecue-logo.png" --project DineCue.Api
dotnet user-secrets set "Email:BrandIconUrl" "https://dinecue.com/dinecue-icon.png" --project DineCue.Api
dotnet user-secrets set "Email:PrivacyUrl" "https://dinecue.com/privacy" --project DineCue.Api
dotnet user-secrets set "Email:TermsUrl" "https://dinecue.com/terms" --project DineCue.Api
dotnet user-secrets set "Email:ResendApiKey" "<resend-api-key>" --project DineCue.Api
dotnet user-secrets set "Email:AppBaseUrl" "<app-base-url>" --project DineCue.Api
```

Recommended production sender:

- From: `DineCue <hello@dinecue.com>`
- Reply-To: a configured DineCue support/contact address

During early setup you may use another verified sender such as `info@coffeedictionary.com`, but keep it environment-configured and temporary. The final production sender should use a DineCue-owned domain.

Welcome emails are sent once after first account creation for email OTP and Google Sign-In users. Delivery failure is logged safely and does not fail sign-in.

Monthly dining recap emails are product notifications. They are disabled by default unless `ProductEmail:MonthlyRecapEnabled` and user/default preferences allow them. Recaps use only stored recommendation history and are recorded in an idempotent delivery ledger so the same user/month is not sent more than once.

## Google Sign-In Setup

Create a Google Cloud project, configure the OAuth consent screen, and create a Web OAuth client ID for the frontend/BFF.

Recommended authorized JavaScript origins:

- `http://localhost:3000`
- `https://dinecue.com`
- `https://www.dinecue.com` if used

Configure the API:

```bash
dotnet user-secrets set "GoogleAuth:Enabled" "true" --project DineCue.Api
dotnet user-secrets set "GoogleAuth:ClientId" "<google-web-client-id>" --project DineCue.Api
dotnet user-secrets set "GoogleAuth:AllowedAudiences:0" "<google-web-client-id>" --project DineCue.Api
```

If `GoogleAuth:AllowedAudiences` is empty, the backend uses `GoogleAuth:ClientId` as the allowed audience. Google credentials are verified only on the backend. Mock Google auth is only for local Development mode when `GoogleAuth:Enabled` is false.

Resend domain/DNS notes:

- Verify the sending domain in Resend before enabling production email.
- Add the DNS records Resend provides for SPF, DKIM, and bounce handling.
- Add a DMARC record for the sending domain.
- Use a restricted backend-only API key.
- Keep the sender address on a verified domain.

## Security Notes

- Provider keys never go to the frontend or mobile app.
- Email provider keys never go to the frontend or mobile app.
- Google credentials are verified by the backend; the API never trusts a client-provided email alone.
- OpenAI and Google Places calls happen only on the backend.
- Store secrets in user-secrets locally and a cloud secret manager in deployed environments.
- Never commit `.env`, real `appsettings` secrets, user-secrets exports, logs, refresh tokens, access tokens, OTP codes, OTP hashes, or provider credentials.
- Refresh tokens and OTP codes must never be persisted in raw form.
- Logs must not include provider keys, authorization headers, prompts with sensitive user data, raw provider responses, or secret values.

## Google Places

Enable Places API New for the Google Cloud project. Restrict the API key to Places API New and only to the environments that need it.

## OpenAI

The OpenAI API key is backend-only. Do not expose it to clients, Swagger examples, logs, or SignalR payloads.
