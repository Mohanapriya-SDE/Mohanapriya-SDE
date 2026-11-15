# KredFlow

KredFlow is a multi-tenant REST API for small money-lending businesses. A **lender** registers, signs in, and manages its own customers, loans, repayment schedules and payments. Every user belongs to exactly one lender, which is the tenant boundary for all business data.

Built with ASP.NET Core (.NET 10), EF Core 10, PostgreSQL, ASP.NET Core Identity and JWT bearer authentication.

## Status

| Area | State |
|---|---|
| Authentication (`register`, `login`, `me`) | Implemented |
| Domain model, migrations, development seed data | Implemented |
| Customer, Loan, Payment and Reporting APIs | Not started |
| Refresh tokens and logout | Not started |
| Lender profile endpoints (`/lenders/me`) | Not started |

## Solution layout

```
KredFlow.slnx
src/
  KredFlow.Domain/          Entities, enums, role constants. No dependencies on other projects.
  KredFlow.Application/     Use cases (AuthService), DTOs, validators, interfaces, exceptions.
  KredFlow.Infrastructure/  EF Core DbContext, migrations, repositories, Identity setup, seeder.
  KredFlow.Api/             Controllers, JWT/auth wiring, error handling, rate limiting, OpenAPI.
tests/
  KredFlow.Tests/           xUnit unit and integration tests (Sqlite in-memory).
```

Dependencies point inward: `Api` -> `Application`/`Infrastructure` -> `Domain`. `Application` defines interfaces (for example `ILenderRepository`, `IUnitOfWork`, `IJwtTokenService`) that `Infrastructure` and `Api` implement.

### Domain model

| Entity | Purpose |
|---|---|
| `Lender` | The tenant: a lending business. Has `BusinessName`, `OwnerName`, `Email` (unique), `PhoneNumber`, `Address`, `TimeZone` (IANA id, default `Asia/Kolkata`), `IsActive`, `CreatedAt`. |
| `ApplicationUser` | An Identity user (`IdentityUser<Guid>`) belonging to one lender. Adds `LenderId`, `FullName`, `IsActive`, `CreatedAt`. Role is an Identity role: `Owner` or `Staff`. |
| `Customer` | A borrower of a lender. |
| `Loan` | Principal, annual interest rate, tenure, EMI, start date and status (`Active`, `Closed`, `Overdue`, `Defaulted`). |
| `RepaymentSchedule` | One row per monthly installment (principal, interest, total due, due date). |
| `Payment` | Money received for a loan (amount, date, method: `Cash`, `UPI`, `BankTransfer`, ...). |
| `PaymentAllocation` | How a payment is split between a schedule row's interest and principal. |
| `LoanTransaction` | Ledger entries for a loan: `Disbursement`, `Repayment`, `Penalty`. |

Conventions: UUID primary keys, money as `decimal` (`NUMERIC(18,2)`), business dates as `DateOnly`, timestamps in UTC, enums stored as strings. Deleting a lender, customer or loan is blocked by restrictive foreign keys, so historical financial data is never lost.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version` should print `10.x`)
- PostgreSQL 14 or later, running locally
- The EF Core CLI, restored from the repo manifest: `dotnet tool restore`

## Getting started

1. **Create or choose a database.** The default connection string in [src/KredFlow.Api/appsettings.json](src/KredFlow.Api/appsettings.json) is:

   ```
   Host=localhost;Port=5432;Database=kredflow_advanced_api;Username=mohanac;
   ```

   Change `Database` and `Username` (and add `Password=...` if your server needs one). To avoid editing a tracked file, override it with an environment variable instead:

   ```bash
   export ConnectionStrings__KredFlow="Host=localhost;Port=5432;Database=kredflow;Username=postgres;Password=postgres"
   ```

   The database itself is created automatically on first run if the user has the `CREATEDB` privilege; otherwise run `createdb <name>` first.

2. **Set the JWT signing key.** The key is a secret and must never be committed. Locally, store it with user-secrets (the project is already initialised):

   ```bash
   dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/KredFlow.Api > /dev/null
   ```

   The key must be at least 32 characters. The app refuses to start without it. In deployed environments, supply it as the `Jwt__SigningKey` environment variable or from a secret manager.

3. **Run the API.**

   ```bash
   dotnet run --project src/KredFlow.Api --launch-profile http
   ```

   | | |
   |---|---|
   | HTTP | `http://localhost:5208` |
   | HTTPS | `https://localhost:7247` (profile `https`) |
   | OpenAPI document | `http://localhost:5208/openapi/v1.json` (Development only) |

   In the `Development` environment the app applies pending migrations and inserts seed data on startup (see below).

4. **Try it.** Open [src/KredFlow.Api/KredFlow.Api.http](src/KredFlow.Api/KredFlow.Api.http) in VS Code (REST Client / HTTP file support) and send the requests, or use `curl` as shown in [Authentication API](#authentication-api).

## Configuration

All settings live in [src/KredFlow.Api/appsettings.json](src/KredFlow.Api/appsettings.json) and can be overridden with environment variables (`:` becomes `__`).

| Key | Default | Description |
|---|---|---|
| `ConnectionStrings:KredFlow` | local Postgres | Npgsql connection string. |
| `Jwt:Issuer` | `KredFlow` | Token issuer. |
| `Jwt:Audience` | `KredFlowClient` | Token audience. |
| `Jwt:AccessTokenExpiryMinutes` | `15` | Access token lifetime. |
| `Jwt:SigningKey` | none (required) | HS256 key, at least 32 characters. Secret. |
| `RateLimiting:Login:PermitLimit` / `WindowSeconds` | `5` / `60` | Login attempts per client IP. |
| `RateLimiting:Register:PermitLimit` / `WindowSeconds` | `3` / `600` | Registrations per client IP. |

## Authentication API

Base path: `/api/v1`. Everything except `register` and `login` requires `Authorization: Bearer <accessToken>`.

| Method and path | Auth | Success | Failure |
|---|---|---|---|
| `POST /api/v1/auth/register` | Public | `201 Created` | `400`, `409`, `429` |
| `POST /api/v1/auth/login` | Public | `200 OK` | `400`, `401`, `429` |
| `GET /api/v1/auth/me` | Bearer | `200 OK` | `401` |

### Register

Creates the lender and its first user (role `Owner`) in a single transaction. Registration does not log the user in.

```bash
curl -X POST http://localhost:5208/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{
    "businessName": "ABC Finance",
    "ownerName": "Ravi Kumar",
    "email": "ravi@example.com",
    "phoneNumber": "+919876543210",
    "password": "StrongPassword@123"
  }'
```

Response `201`:

```json
{
  "userId": "8f3d...",
  "lenderId": "7b2a...",
  "businessName": "ABC Finance",
  "ownerName": "Ravi Kumar",
  "email": "ravi@example.com",
  "message": "Registration successful"
}
```

Validation rules:

| Field | Rule |
|---|---|
| `businessName` | Required, max 200 characters |
| `ownerName` | Required, max 150 characters |
| `email` | Required, valid format, max 255. Trimmed and lowercased; must be unique |
| `phoneNumber` | Required Indian mobile number (starts with 6-9, 10 digits), optional `+91`. Stored as `+91XXXXXXXXXX` |
| `password` | 12-128 characters with an upper-case letter, a lower-case letter, a digit and a symbol |

### Login

```bash
curl -X POST http://localhost:5208/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{ "email": "ravi@example.com", "password": "StrongPassword@123" }'
```

Response `200`:

```json
{
  "accessToken": "<jwt>",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "user": {
    "userId": "8f3d...",
    "lenderId": "7b2a...",
    "fullName": "Ravi Kumar",
    "email": "ravi@example.com",
    "role": "Owner",
    "businessName": "ABC Finance"
  }
}
```

`expiresIn` is in seconds. Every login failure returns the same `401` response, so the API never reveals whether an account exists:

- unknown email, wrong password, or a locked account
- inactive user
- inactive lender

After 5 consecutive wrong passwords the account is locked for 15 minutes (ASP.NET Core Identity lockout), even if the correct password is then supplied.

### Current user

```bash
curl http://localhost:5208/api/v1/auth/me -H "Authorization: Bearer $TOKEN"
```

Returns the same user object as in the login response. The identity comes from the token, never from request parameters.

### Access token

An HS256 JWT with these claims:

| Claim | Meaning |
|---|---|
| `sub` | User id |
| `email` | User email |
| `lenderId` | Tenant id; business APIs must scope all data by this value |
| `role` | `Owner` or `Staff` |
| `jti` | Unique token id |
| `exp`, `nbf`, `iat` | Expiry, not-before and issued-at |

On every request the API also checks that the user and lender are still active, so deactivating an account takes effect immediately instead of when the token expires. Access tokens cannot be revoked individually; they are short-lived by design.

## Error responses

Errors use `application/problem+json` (RFC 9457) with a `traceId` for log correlation.

| Status | When |
|---|---|
| `400 Bad Request` | Invalid request. Includes an `errors` object keyed by field name |
| `401 Unauthorized` | Missing or invalid token, or failed login |
| `403 Forbidden` | Authenticated but not permitted |
| `404 Not Found` | Resource not found within the caller's tenant |
| `409 Conflict` | Conflicting operation, for example an email that is already registered |
| `422 Unprocessable Entity` | Valid request that breaks a business rule |
| `429 Too Many Requests` | Rate limit exceeded; see the `Retry-After` header |
| `500 Internal Server Error` | Unexpected failure |

Example `400`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "password": ["'Password' must contain a symbol."] },
  "traceId": "0HNQ29..."
}
```

## Cross-cutting conventions

| Topic | Decision |
|---|---|
| Versioning | URL segment: `/api/v1/...` |
| Authentication | JWT bearer; the fallback policy requires an authenticated user, so new endpoints are protected unless marked `[AllowAnonymous]` |
| Validation | FluentValidation, run by a global action filter |
| Rate limiting | Built-in ASP.NET Core rate limiter, fixed window per client IP, on login and registration |
| Transport | HTTPS redirection everywhere; HSTS outside Development |
| Logging | Passwords, tokens and request bodies are never logged. Keep EF sensitive-data logging off |

## Database and migrations

Migrations live in [src/KredFlow.Infrastructure/Persistence/Migrations](src/KredFlow.Infrastructure/Persistence/Migrations). Identity tables are named `Users`, `Roles`, `UserRoles` and so on, to match the rest of the schema. The `Owner` and `Staff` roles are seeded by the migration.

```bash
# Add a migration
dotnet ef migrations add <Name> --project src/KredFlow.Infrastructure --output-dir Persistence/Migrations

# Apply migrations to the database
dotnet ef database update --project src/KredFlow.Infrastructure
```

The `dotnet ef` commands use `KredFlowDbContextFactory`, which has its own hardcoded connection string (`localhost`, database `kredflow_advanced_api`, user `mohanac`). Edit [KredFlowDbContextFactory.cs](src/KredFlow.Infrastructure/Persistence/KredFlowDbContextFactory.cs) to match your database before running them. Running the API in Development applies migrations by itself.

## Development seed data

In the `Development` environment, `DatabaseSeeder` inserts sample data once, when the `Lenders` table is empty:

- 2 lenders, each with an owner login
- 10 customers and 15 loans, covering the closed, active, overdue and defaulted statuses
- Repayment schedules, payments, allocations and ledger transactions for those loans

Seed lenders and logins (password for both: `KredFlow@Dev2026!`):

| Business | Login email |
|---|---|
| Annapoorna Finance | `contact@annapoorna-finance.example` |
| Lakshmi Micro Credit | `hello@lakshmi-microcredit.example` |

That password is public. Seeding never runs outside Development.

Because the seeder skips a database that already contains lenders, a database created before the auth migration will have lenders without logins. For a clean local start, recreate it: `dotnet ef database drop --project src/KredFlow.Infrastructure`, then run the API again. This deletes all data in that database.

## Tests

```bash
dotnet test KredFlow.slnx
```

- `DatabaseSeederTests` covers the seed data and its determinism.
- `AuthApiTests` runs the real API over HTTP against in-memory Sqlite: registration, duplicate emails, validation, login claims, uniform `401` failures, lockout, `me`, and rejection of tokens after a lender is deactivated.
- `AuthEdgeCaseTests` covers rate limiting, startup without a signing key, transaction rollback, phone normalization and the password policy.

Integration tests run with the `Testing` environment, which skips the Development migrate-and-seed step. The tests create the schema with `EnsureCreated`, because the migrations are specific to PostgreSQL.

## Roadmap

- Customer, Loan, Payment and Reporting endpoints, with all queries scoped to the caller's `lenderId`
- Refresh tokens (hashed at rest, rotated on use) and logout
- Lender profile endpoints: `GET` and `PUT /api/v1/lenders/me`, and deactivation
- Business-date calculations in the lender's time zone
