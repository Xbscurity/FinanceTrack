# FinanceTrack API

A REST API for tracking personal finances: expense/income categories, transactions, and reports by period and category. Built with ASP.NET Core, featuring JWT authentication, resource-based authorization, and full unit + integration test coverage.

![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-15-4169E1?logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker&logoColor=white)
![CI](https://github.com/Xbscurity/FinanceTrack/actions/workflows/ci.yml/badge.svg)
![License](https://img.shields.io/badge/license-MIT-green)

## Tech Stack

- **ASP.NET Core 10** — Web API
- **Entity Framework Core** + **PostgreSQL** — data access, code-first migrations
- **ASP.NET Core Identity** + **JWT (access + refresh tokens)** — authentication
- **Ardalis.Specification** — Specification pattern for filtering, sorting, and paging queries
- **ErrorOr** — consistent error handling without relying on exceptions in business logic
- **ZiggyCreatures.FusionCache** — caching (e.g. user ban status)
- **Serilog** + **Seq** — structured logging
- **Swashbuckle (Swagger/OpenAPI)** — auto-generated API documentation
- **xUnit** — unit and integration tests (`WebApplicationFactory`)
- **Docker / docker-compose** — containerized stack (API, PostgreSQL, pgAdmin, Seq)
- **GitHub Actions** — CI: build + test run on every push/PR

## Features

- Registration, login, token refresh, and logout (JWT access + refresh token)
- Password change and viewing your own profile
- CRUD for categories and financial transactions; categories can be soft-deactivated via a dedicated endpoint, or hard-deleted if they have no related transactions
- Reports on transactions — by category, by date range, and by both combined
- Separate endpoints for regular users and administrators (`/api/...` and `/api/admin/...`)
- User management and bans via a custom `IAuthorizationRequirement` that checks ban status on every request
- Resource-based authorization — users can only access their own categories/transactions, not just role checks
- Refresh tokens are single-use with rotation and automatic reuse detection: a reused (stolen) refresh token revokes the entire token family
- JWT signing algorithm explicitly restricted to `HMACSHA512` to prevent algorithm-confusion attacks
- Account lockout (5 failed attempts / 15 min) on login and password change, plus rate limiting — a global limit on all requests and a stricter one for auth endpoints
- A single, consistent error format via a global exception handler and `ErrorOr`
- Automatic migration application and admin seeding on startup
- Architecture tests that enforce the project's structural rules (e.g. no endpoint can be anonymous unless explicitly whitelisted)

## API

Full interactive documentation is available via Swagger UI at `/swagger` once the app is running.

| Method | Route | Description |
|---|---|---|
| POST | `/api/auth/register` | Register a new user |
| POST | `/api/auth/login` | Log in, issue access + refresh tokens |
| POST | `/api/auth/refresh` | Refresh the access token |
| POST | `/api/auth/logout` | Log out, revoke the refresh token |
| GET | `/api/account/me` | Current user's profile |
| POST | `/api/account/change-password` | Change password |
| GET/POST/PUT/PATCH/DELETE | `/api/categories` | Category CRUD |
| GET/POST/PUT/DELETE | `/api/financial-transactions` | Transaction CRUD |
| GET | `/api/financial-transactions/report` | Transaction report |
| GET | `/api/users` | List users (admin) |
| POST | `/api/users/{userId}/ban-status` | Ban/unban a user (admin) |
| ... | `/api/admin/categories`, `/api/admin/financial-transactions` | Admin-facing CRUD versions |

## Quick Start (Docker)

The easiest way to run the full stack — API, PostgreSQL, pgAdmin, and Seq for logs.

**Requirements:** Docker and Docker Compose installed.

```bash
git clone https://github.com/Xbscurity/FinanceTrack.git
cd FinanceTrack
cp .env.example .env
cp api/appsettings.example.json api/appsettings.json
```

Fill in `JWT:SigningKey` and `Seed` credentials in `appsettings.json`, and replace the `replace-me` placeholders in `.env`, then:

```bash
docker-compose up --build
```

Once running:

- API and Swagger — [http://localhost:5000/swagger](http://localhost:5000/swagger)
- pgAdmin — http://localhost:8081 (log in with PGADMIN_EMAIL / PGADMIN_PASSWORD from your .env). To browse the database, register a new server (right-click **Servers** → **Register** → **Server**) with host `postgres`, port `5432`, and the `POSTGRES_USER` / `POSTGRES_PASSWORD` from your `.env`.
- Seq (logs) — http://localhost:5341 (log in as admin with SEQ_PASSWORD from your .env)

## Running Locally Without Docker

**Requirements:** .NET SDK 10, a local or remote PostgreSQL instance.

```bash
git clone https://github.com/Xbscurity/FinanceTrack.git
cd FinanceTrack/api
cp appsettings.example.json appsettings.Development.json
```

Fill in your PostgreSQL connection string and the remaining values (JWT signing key, admin seed credentials, etc.) in `appsettings.Development.json`, then:

```bash
dotnet restore
dotnet run
```

Migrations are applied automatically on startup.

## Tests

```bash
dotnet test
```

The project includes:

- **Unit tests** — services, specifications, custom validation attributes
- **Integration tests** — controllers and authorization via `WebApplicationFactory`, using a real PostgreSQL instance via Testcontainers
- **Architecture tests** — enforce that the codebase's structure doesn't drift from the intended rules

The CI pipeline (GitHub Actions) automatically builds and runs all tests on every push and pull request to `main`.

## Project Structure

```
api/
├── Controllers/       — endpoints (regular + admin versions)
├── Services/           — business logic
├── Repositories/        — data access
├── Specifications/      — filtering/sorting/paging specifications (Ardalis.Specification)
├── Dtos/                — request/response models
├── Models/              — domain entities
├── Authorization/       — custom authorization policies (e.g. ban check)
├── Middlewares/         — global exception handling, log enrichment
├── Data/                — DbContext, seeding logic
├── Migrations/          — EF Core migrations
└── Program.cs           — entry point and service composition

api.Tests.Unit/          — unit tests
api.Tests.Integration/   — integration tests
```

## Engineering Decisions

The following decisions are intentional:

- **CORS is not configured.** The API currently has no browser-based frontend. CORS can be added when a frontend client is introduced.
- **HTTPS redirection** is handled outside the application. Local development uses HTTP; production deployments are expected to terminate TLS at a reverse proxy or hosting platform.
- **Cancellation tokens** are not currently propagated through the application layers. This is a known improvement planned for a future iteration.


## License

MIT — see [LICENSE](./LICENSE) for details.
