# DineCue API

DineCue API is the backend for the DineCue mobile experience. It handles authentication, profiles, restaurant/recommendation workflows, async recommendation generation, and realtime status notifications.

## Tech Stack

- ASP.NET Core
- PostgreSQL
- Entity Framework Core
- JWT authentication
- SignalR
- Google Places API New
- OpenAI API

## Local Prerequisites

- .NET SDK compatible with the solution target framework
- Docker Desktop or a local PostgreSQL instance
- Google Cloud project with Places API New enabled
- OpenAI API access

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
- `Quotas:RecommendationDailyFree`
- `Quotas:RecommendationDailyPro`

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
dotnet user-secrets set "Quotas:RecommendationDailyFree" "5" --project DineCue.Api
dotnet user-secrets set "Quotas:RecommendationDailyPro" "50" --project DineCue.Api
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

## Async Recommendation Flow

- `POST /recommendation-sessions` authenticates the user, validates the request, creates a session, queues a background job, and returns `202 Accepted`.
- The background worker calls backend-only Google Places and OpenAI providers, then persists recommendations.
- `GET /recommendation-sessions/{id}` is the source of truth for status, errors, and results.
- SignalR at `/hubs/recommendations` only sends status notifications. It does not send full recommendation data.

## Security Notes

- Provider keys never go to the frontend or mobile app.
- OpenAI and Google Places calls happen only on the backend.
- Store secrets in user-secrets locally and a cloud secret manager in deployed environments.
- Never commit `.env`, real `appsettings` secrets, user-secrets exports, logs, refresh tokens, access tokens, OTP codes, OTP hashes, or provider credentials.
- Refresh tokens and OTP codes must never be persisted in raw form.
- Logs must not include provider keys, authorization headers, prompts with sensitive user data, raw provider responses, or secret values.

## Google Places

Enable Places API New for the Google Cloud project. Restrict the API key to Places API New and only to the environments that need it.

## OpenAI

The OpenAI API key is backend-only. Do not expose it to clients, Swagger examples, logs, or SignalR payloads.
