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
- OpenAI API (text and vision)
- Google Cloud Storage (private bucket for menu photos, via Application Default Credentials)

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
- `Storage:Enabled`
- `Storage:Bucket` (required when `Storage:Enabled` is true)

Optional keys with safe defaults:

- `OpenAI:VisionModel` — falls back to `OpenAI:Model` when blank.
- `OpenAI:UseStructuredOutputs` — strict `json_schema` responses. Set to `false` to fall back to plain JSON mode without a code change.
- `OpenAI:MaxTransportAttempts` — retries for network faults, 429s, and 5xx responses.
- `GooglePlaces:SnapshotFreshnessHours`, `GooglePlaces:SearchCacheMinutes`, `GooglePlaces:GeocodeCacheHours` — cache lifetimes that keep the richer field mask affordable.
- `Storage:ObjectPrefix`, `Storage:MaxImageBytes`, `Storage:MaxImagesPerScan`, `Storage:AllowedContentTypes`, `Storage:OrphanImageRetentionHours`.

`AllowedOrigins` and `Swagger:Enabled` are present in the example configuration but are **not wired up**. There is no CORS middleware and no Swagger middleware; the UI reaches this API only through its own server-side BFF, so CORS is not needed.

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

`Recommendation:UseMockProvider` switches every AI/provider surface at once: candidate search, location resolution, reservation links, menu interpretation, and restaurant fit analysis. Menu photo storage is gated separately by `Storage:Enabled`, so menu scanning can be enabled or disabled without touching the recommendation flow.

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

Health probe for Cloud Run and local checks:

```text
GET http://localhost:5000/health
```

It returns `200 {"status":"ok"}` when the database answers and `503 {"status":"degraded"}` otherwise. It is anonymous and exposes no build or dependency details.

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

## Menu Scan Flow

Menu scanning is asynchronous for the same reason recommendations are: reading photos with a vision model takes long enough that holding the request open is fragile on a phone.

1. `POST /uploads/menu-images` (multipart, field name `file`) stores one photo and returns its `objectName`. Up to `Storage:MaxImagesPerScan` photos per scan, because menus span several pages. **Uploading costs no monthly credit** so retries are free.
2. `POST /menu-scans` with `imageObjectNames` plus optional context (`rawText`, `selectedCues`, `context`, `restaurantPlaceId`) returns `202 Accepted`. This is where the single credit is reserved.
3. `GET /menu-scans/{id}` is the source of truth for `status`, `currentStep`, `errorCode`, and results.
4. `GET /menu-scans/{id}/images/{index}` streams a stored photo back to its owner. The bucket is private and object names are never returned to clients.
5. `POST /menu-scans/{id}/recommend` re-ranks an already-read menu against the diner's current profile. **No credit**, because no image is read again.

Context is optional by design. Allergies, dietary restrictions, disliked ingredients, spice tolerance, kids, and budget come from the stored taste and dining profiles, so the diner never has to retype them. Anything they write is the moment-specific layer on top.

Two safety rules are enforced in code, not left to the model:

- The allergen disclaimer is always appended in the diner's language.
- Any suggested dish that conflicts with a stored allergy or dietary restriction is removed from the suggestions and surfaced as a warning instead.

An unreadable photo fails with `menu_not_readable` and **returns the credit**.

`imageUrl` is no longer accepted as an image source. Fetching an arbitrary caller-supplied URL is unnecessary risk now that uploads have a dedicated endpoint; sending it returns `validation_error`.

## Restaurant Fit Check Flow

`POST /restaurants/{placeId}/fit-check` answers "does this specific place suit this moment?" for one credit. It is synchronous: one Places lookup plus one model call.

The place is addressed by Google place id, which reaches the client three ways:

- From a recommendation card (`providerPlaceId` on the card) or a saved place — the zero-friction path.
- From `POST /restaurants/search` by name, with the same three location modes the find form already uses.
- From the same search with the `nearby` cue, which ranks by distance instead of rating.

Optional `context.visitAt` (ISO-8601) is the single most valuable input after the place itself, because opening hours are the most concrete signal available.

The verdict grounds only on provider-reported attributes (`goodForChildren`, `goodForGroups`, `outdoorSeating`, `reservable`, `servesVegetarianFood`, opening hours, price level, parking, accessibility, editorial summary) crossed with the stored profile. `relevantSignals` reports which signals moved the score.

`POST /restaurants/search` and `GET /restaurants/{placeId}` now **require authentication**. They call the paid Places API, and leaving them anonymous would be an unmetered cost and abuse surface.

## Object Storage

Menu photos live in a private Google Cloud Storage bucket. Access uses Application Default Credentials, so on Cloud Run the runtime service account is the only thing needed — **no new secret is introduced**.

Setup:

1. Create a private bucket (no public access, no allUsers binding).
2. Grant the Cloud Run runtime service account `roles/storage.objectAdmin` on that bucket.
3. Set `Storage__Enabled=true` and `Storage__Bucket=<bucket-name>` on the Cloud Run service.

`cloudbuild.yaml` deliberately does not carry environment variables or secrets; runtime configuration is managed on the Cloud Run service itself.

With `Storage:Enabled=false` the app uses in-process storage and mock menu interpretation. Menu scanning degrades safely and the recommendation flow is completely unaffected, which makes this the staged-rollout switch.

## Monthly Quotas

DineCue uses backend-enforced monthly usage credits. The default free plan has 5 credits per calendar month and the future/manual Pro plan has 50 credits per calendar month. Public upgrade, checkout, Stripe, and billing flows are not implemented yet.

Quota-consuming actions share one monthly pool. One credit is a dining decision, not an API call:

| Action | Credits |
| --- | --- |
| `POST /recommendation-sessions` | 1 |
| `POST /recommendation-sessions/{id}/refine` | 1 |
| `POST /uploads/menu-images` | 0 |
| `POST /menu-scans` (any number of photos) | 1 |
| `POST /menu-scans/{id}/recommend` | 0 |
| `POST /restaurants/{placeId}/fit-check` | 1 |
| `POST /restaurants/search`, `GET /restaurants/{placeId}` | 0 |

A reserved credit is always released when the work fails.

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

## Known Operational Risks

These are understood and deliberately not addressed yet. They are recorded here so nobody rediscovers them during an incident.

**Background job queues are in-process.** Both the recommendation and menu scan queues are unbounded in-memory `Channel`s read by a single-threaded worker inside the API process. Consequences:

- A job accepted with `202` is lost if the instance is shut down before it runs. Each worker re-queues anything left in `queued` or `running` on startup, which recovers the job on the next cold start, but there is no guarantee of when that happens.
- With `min-instances=0` a queued job may sit unprocessed until the next request wakes an instance.
- The startup re-queue is global, not scoped to the instance, so with more than one instance several workers can pick up the same stale job.

Mitigation while this stands: run the service with `min-instances=1` and a single instance, or accept the recovery-on-restart behaviour. The durable fix is a claim/lease column on the session and scan rows so work can be handed out safely across instances.

**Quota reservation is check-then-increment.** `QuotaService.ReserveAsync` reads current usage, compares it to the limit, then increments, without a row lock or a serializable transaction. Two truly concurrent requests from the same user can both pass the check and overshoot the monthly limit by one. The per-endpoint rate limiters make this narrow in practice. The durable fix is a conditional atomic `UPDATE` plus retry on the unique-index conflict.

**OTP rate limiting is per instance.** `OtpRateLimiter` uses `IMemoryCache`, so its counters are not shared between instances. The database-backed abuse-protection checks are shared and remain effective.

**Migrations are hand-written SQL with no model snapshot.** `dotnet ef migrations add` cannot be used incrementally, and EF model-versus-database parity is not machine-verified. New columns and indexes must be written by hand. The initial migration had already drifted from the model this way: the unique index on `restaurant_snapshots (Provider, ProviderPlaceId)` existed in the model but not in the database until `20260807120000_AddMenuScanJobAndImages` created it.

**SignalR is written but not wired.** `RecommendationHub` and `SignalRRecommendationStatusNotifier` are complete but unregistered; the app registers a no-op notifier. The UI polls `GET /recommendation-sessions/{id}` on purpose, because a direct browser-to-backend socket would bypass its BFF boundary.

## Google Places

Enable Places API New for the Google Cloud project. Restrict the API key to Places API New and only to the environments that need it.

The candidate field mask requests the richer place attributes (opening hours and periods, `goodForChildren`, `goodForGroups`, `outdoorSeating`, `reservable`, `servesVegetarianFood`, parking, accessibility, editorial summary, phone). This is what lets the model make concrete, grounded claims instead of vague ones — the prompt forbids claiming anything the metadata does not support, so thin metadata produced deliberately vague copy.

Those fields sit in a more expensive Places SKU, so three caches keep the cost bounded:

- Text-search result pools are cached in process for `GooglePlaces:SearchCacheMinutes`, so refining a session does not pay twice.
- Every normalized place is upserted into `restaurant_snapshots`, and place-details reads are served from there while `GooglePlaces:SnapshotFreshnessHours` has not elapsed. Fit-checking a place a recommendation run already fetched costs nothing extra.
- Resolved free-text locations are cached for `GooglePlaces:GeocodeCacheHours`.

Free-text locations are geocoded through Places text search. If a lookup fails the request proceeds **without** coordinates rather than with substituted ones, and the place name stays in the query text.

## OpenAI

The OpenAI API key is backend-only. Do not expose it to clients, Swagger examples, logs, or SignalR payloads.
