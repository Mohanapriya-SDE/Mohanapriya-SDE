# KredFlow

KredFlow is a multi-tenant REST API for small money-lending businesses. A **lender** registers, signs in, and manages its own customers, loans, repayment schedules and payments. Every user belongs to exactly one lender, which is the tenant boundary for all business data.

Built with ASP.NET Core (.NET 10), EF Core 10, PostgreSQL, ASP.NET Core Identity and JWT bearer authentication.

## Status

| Area | State |
|---|---|
| Authentication (`register`, `login`, `me`) | Implemented |
| Lender profile (`GET`/`PUT /lenders/me`) and deactivation | Implemented |
| Domain model, migrations, development seed data | Implemented |
| Customer management API (`/customers`) | Implemented |
| Loan, Payment and Reporting APIs | Not started |
| Refresh tokens and logout | Not started |

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
| `Customer` | A borrower of a lender. Has `FullName`, `PhoneNumber` (not unique), `Address`, `IsActive` (soft deactivation) and `CreatedAt`. |
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
| `Lenders:SupportedTimeZones` | `["Asia/Kolkata"]` | Time zone ids a lender may choose. Must include `Asia/Kolkata` (the registration default) and only ids known to the host OS; the app fails to start otherwise. |
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

## Lender API

The caller's lender is taken from the token's `lenderId` claim, so there is no lender id in the URL. A lender is created by [registration](#register), not by this API.

| Method and path | Role | Success | Failure |
|---|---|---|---|
| `GET /api/v1/lenders/me` | Any user of the lender | `200 OK` | `401` |
| `PUT /api/v1/lenders/me` | `Owner` | `200 OK` | `400`, `401`, `403` |
| `PATCH /api/v1/lenders/me/status` | `Owner` | `204 No Content` | `401`, `403`, `422` |

`LenderResponse`:

```json
{
  "id": "7b2a...",
  "businessName": "ABC Finance",
  "ownerName": "Ravi Kumar",
  "email": "ravi@example.com",
  "phoneNumber": "+919876543210",
  "address": "Bangalore",
  "timeZone": "Asia/Kolkata",
  "isActive": true,
  "createdAt": "2026-11-12T10:00:00Z"
}
```

### Update profile

`PUT /api/v1/lenders/me` replaces the four editable fields. `email` and `ownerName` cannot be changed here.

```json
{
  "businessName": "Sri Lakshmi Finance",
  "phoneNumber": "9876543210",
  "address": "Chennai",
  "timeZone": "Asia/Kolkata"
}
```

| Field | Rule |
|---|---|
| `businessName` | Required, max 200 characters |
| `phoneNumber` | Indian mobile number, optional `+91`; stored as `+91XXXXXXXXXX` |
| `address` | Optional, max 500 characters |
| `timeZone` | Required; must be one of `Lenders:SupportedTimeZones` |

### Deactivate

`PATCH /api/v1/lenders/me/status` with `{ "isActive": false }` deactivates the lender. Nothing is deleted: customers, loans, payments and transactions are kept.

- From then on, every user of the lender gets `401` on login and on any existing token.
- Reactivation is not available through the API (an inactive lender cannot authenticate), so `{ "isActive": true }` returns `422`. Reactivate by setting `Lenders.IsActive` back to `true` in the database.
- Deactivation returns `422` while the lender still has `Active` or `Overdue` loans.

## Customer API

Every customer belongs to the caller's lender, taken from the token's `lenderId` claim (never from the request). A customer of another lender is reported as `404`, the same as an unknown id. All endpoints require a valid token (any role).

| Method and path | Success | Failure |
|---|---|---|
| `POST /api/v1/customers` | `201 Created` with `Location` | `400`, `401` |
| `GET /api/v1/customers/{customerId}` | `200 OK` | `401`, `404` |
| `GET /api/v1/customers?search=&isActive=&page=&pageSize=` | `200 OK` (`PagedResponse`) | `400`, `401` |
| `PUT /api/v1/customers/{customerId}` | `200 OK` | `400`, `401`, `404` |
| `PATCH /api/v1/customers/{customerId}/status` | `204 No Content` | `401`, `404` |
| `GET /api/v1/customers/{customerId}/loans` | `200 OK` (newest first) | `401`, `404` |
| `GET /api/v1/customers/{customerId}/outstanding` | `200 OK` | `401`, `404` |

`CustomerResponse`:

```json
{
  "id": "3f9c...",
  "fullName": "Meena Devi",
  "phoneNumber": "+919123456789",
  "address": "Chennai",
  "isActive": true,
  "createdAt": "2026-11-16T10:00:00Z"
}
```

| Field | Rule |
|---|---|
| `fullName` | Required, max 200 characters |
| `phoneNumber` | Indian mobile number, optional `+91`; stored as `+91XXXXXXXXXX`. Not unique, so several customers may share a number |
| `address` | Optional, max 500 characters |

### Create and update

`POST /api/v1/customers` takes the body below and returns `201` with a `CustomerResponse` and a `Location` header pointing at `GET /api/v1/customers/{id}`. `PUT /api/v1/customers/{customerId}` takes the same three fields, replaces all of them, and returns `200` with the updated `CustomerResponse`.

```json
{
  "fullName": "Meena Devi",
  "phoneNumber": "9123456789",
  "address": "Chennai"
}
```

- Input is trimmed. A blank `address` is stored as `null`.
- A new customer is always active and always owned by the caller's lender. `lenderId`, `isActive` and `createdAt` in the body are ignored.
- Phone numbers are not unique, so creating a second customer with the same number succeeds.

### List and search

`GET /api/v1/customers` returns only the caller's customers, ordered by name.

| Query parameter | Default | Rule |
|---|---|---|
| `search` | none | Max 100 characters. Case-insensitive match on name, or a match on the stored phone number (for example `9333` or `+9193`) |
| `isActive` | none (both) | `true` or `false` |
| `page` | `1` | 1 or more |
| `pageSize` | `20` | 1 to 100 |

```json
{
  "items": [ { "id": "3f9c...", "fullName": "Anita Rao", "phoneNumber": "+919111111111", "address": "Chennai", "isActive": true, "createdAt": "2026-11-16T10:00:00Z" } ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 3,
  "totalPages": 1
}
```

An out-of-range `page` or `pageSize` returns `400`. A page past the end returns `200` with an empty `items` list.

### Activate and deactivate

`PATCH /api/v1/customers/{customerId}/status` with `{ "isActive": false }` deactivates and `{ "isActive": true }` reactivates. Both return `204`, and repeating the same request is harmless.

- Nothing is deleted. Loans, payments and transactions of an inactive customer stay readable.
- Deactivation is allowed while the customer has open loans (unlike lenders, which are blocked).
- Rejecting new loans for an inactive customer is enforced when loan creation is added; the flag is already stored for it.

### Loan history

`GET /api/v1/customers/{customerId}/loans` returns a JSON array, newest `startDate` first. It includes loans of every status, and is empty for a customer with no loans.

```json
[
  {
    "id": "a1d4...",
    "principal": 10000,
    "annualInterestRate": 12,
    "tenureMonths": 12,
    "emiAmount": 900,
    "startDate": "2026-01-01",
    "status": "Active"
  }
]
```

### Outstanding

`GET /api/v1/customers/{customerId}/outstanding`:

```json
{ "customerId": "3f9c...", "openLoanCount": 2, "totalOutstanding": 10900 }
```

- Only `Active` and `Overdue` loans count; `Closed` and `Defaulted` loans are excluded.
- Per loan it is the sum of the schedule's `TotalDue` minus the allocated payments (interest plus principal), never below zero.
- A loan with no repayment schedule yet counts its full principal.
- A customer with no open loans returns `0` for both numbers.

### Errors

| Status | When |
|---|---|
| `400` | Blank or too long `fullName`, invalid `phoneNumber`, `address` over 500 characters, or an invalid list query. The body is a validation problem with an `errors` object keyed by field |
| `401` | Missing, invalid or expired token |
| `404` | The id does not exist **or belongs to another lender**. Both cases are deliberately identical, so ids cannot be probed across lenders |

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

| Migration | Creates |
|---|---|
| `InitialCreate` | Lenders, customers, loans, repayment schedules, payments, payment allocations, loan transactions |
| `AddIdentityAndLenderProfile` | Identity tables, the `Owner` and `Staff` roles, and the lender profile columns |

The `dotnet ef` commands use `KredFlowDbContextFactory`, which has its own hardcoded connection string (`localhost`, database `kredflow_advanced_api`, user `mohanac`). It is not read from `appsettings.json`. Edit [KredFlowDbContextFactory.cs](src/KredFlow.Infrastructure/Persistence/KredFlowDbContextFactory.cs) so it matches the database the API uses. Run all commands from the repository root, after `dotnet tool restore`.

### Apply migrations

Running the API in `Development` creates the database, applies pending migrations and seeds data, so you usually do not need to apply them yourself. To do it manually:

```bash
dotnet ef migrations list --project src/KredFlow.Infrastructure                      # applied or pending
dotnet ef database update --project src/KredFlow.Infrastructure                      # apply all pending
dotnet ef database update InitialCreate --project src/KredFlow.Infrastructure        # apply up to one migration
dotnet ef migrations script --project src/KredFlow.Infrastructure                    # print SQL only
psql -U mohanac -d kredflow_advanced_api -c '\dt'                                    # check the tables
```

### Add a migration

```bash
dotnet ef migrations add <Name> --project src/KredFlow.Infrastructure --output-dir Persistence/Migrations
```

Review the generated file. A renamed property is scaffolded as drop-and-add unless edited to `RenameColumn`, and new non-null columns on tables that already have rows need a default.

### Remove a migration

If the migration has been applied, revert the database first, then remove the files:

```bash
dotnet ef database update <PreviousMigrationName> --project src/KredFlow.Infrastructure
dotnet ef migrations remove --project src/KredFlow.Infrastructure
```

To remove `AddIdentityAndLenderProfile`, the previous migration is `InitialCreate`. Reverting drops the Identity tables and the lender profile columns and deletes their data. `dotnet ef database update 0` reverts everything. Never remove a migration that is applied to a shared or production database; add a new migration that undoes it.

### Reset the local database

These commands delete all data. Use them on a local development database only.

```bash
dotnet ef database drop --project src/KredFlow.Infrastructure        # add -f to skip the prompt
dropdb -U mohanac kredflow_advanced_api                              # same, using PostgreSQL directly
psql -U mohanac -d kredflow_advanced_api -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'   # keep the database, drop all tables
```

The next `dotnet run` in Development recreates, migrates and reseeds.

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

Because the seeder skips a database that already contains lenders, a database created before the auth migration will have lenders without logins. For a clean local start, reset it as described in [Reset the local database](#reset-the-local-database).

## Testing authentication manually

The automated tests cover most of these scenarios (see [Tests](#tests)). You can also send the requests from [KredFlow.Api.http](src/KredFlow.Api/KredFlow.Api.http) in VS Code.

### Setup

The login rate limit is 5 per minute per IP, and the lockout test needs 6 attempts, so raise it while testing:

```bash
export RateLimiting__Login__PermitLimit=100
dotnet run --project src/KredFlow.Api --launch-profile http
```

In a second terminal (these variables must be set again in each new terminal):

```bash
B=http://localhost:5208/api/v1/auth
J='Content-Type: application/json'
E="test$RANDOM@example.com"
P='StrongPassword@123'
```

Put JSON in single quotes and splice variables in with `'"$VAR"'`. A curl status of `[000]` means the API is not reachable or `$B` is empty. A `400` saying `The request field is required` means the JSON is malformed.

### 1. Register

```bash
curl -s -w ' [%{http_code}]\n' -X POST $B/register -H "$J" \
  -d '{"businessName":"ABC","ownerName":"Ravi","email":"'"$E"'","phoneNumber":"9876543210","password":"'"$P"'"}'
```

| Parameter | Rule |
|---|---|
| `businessName` | Required, max 200 characters |
| `ownerName` | Required, max 150 characters |
| `email` | Valid, max 255, unique (case-insensitive) |
| `phoneNumber` | Indian mobile (starts 6-9, 10 digits), optional `+91` |
| `password` | 12-128 characters with upper-case, lower-case, digit and symbol |

Expected: `201` with `userId` and `lenderId`.

**Duplicate email** (same email upper-cased): expect `409`.

```bash
curl -s -w ' [%{http_code}]\n' -X POST $B/register -H "$J" \
  -d '{"businessName":"ABC","ownerName":"Ravi","email":"'"$(echo $E | tr a-z A-Z)"'","phoneNumber":"9876543210","password":"'"$P"'"}'
```

**Weak password**: expect `400` with several messages under `errors.password`.

```bash
curl -s -w ' [%{http_code}]\n' -X POST $B/register -H "$J" \
  -d '{"businessName":"ABC","ownerName":"Ravi","email":"other'"$E"'","phoneNumber":"9876543210","password":"weakpass"}'
```

Each of these fails one rule: `Short1!` (too short), `alllowercase123!` (no upper-case), `ALLUPPERCASE123!` (no lower-case), `NoDigitsHere!!!!` (no digit), `NoSymbolsHere1234` (no symbol). An empty `businessName` or phone `123` also returns `400`.

### 2. Login

```bash
R=$(curl -s -X POST $B/login -H "$J" -d '{"email":"'"$E"'","password":"'"$P"'"}'); echo $R
T=$(echo $R | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')
```

| Parameter | Rule |
|---|---|
| `email` | Required, valid format |
| `password` | Required |

Expected: `200` with `accessToken`, `tokenType: Bearer`, `expiresIn: 900` and a `user` block with role `Owner`.

| Scenario | Change | Expected |
|---|---|---|
| Wrong password | `"password":"nope"` | `401`, "Invalid email or password." |
| Unknown email | `"email":"nobody@example.com"` | `401`, same body as above, so the response does not reveal whether an account exists |
| Malformed email | `"email":"not-an-email"` | `400` |
| Seeded login | `contact@annapoorna-finance.example` / `KredFlow@Dev2026!` | `200` on a database seeded after the auth migration |

Inspect the token with `echo $T | cut -d. -f2 | base64 -d`. Claims are `sub`, `email`, `lenderId`, `role`, `jti` and `exp` (15 minutes after `iat`).

### 3. Current user

```bash
curl -s -w ' [%{http_code}]\n' $B/me -H "Authorization: Bearer $T"          # expect 200
curl -s -i $B/me                                                              # no token: expect 401 with WWW-Authenticate: Bearer
curl -s -w ' [%{http_code}]\n' $B/me -H "Authorization: Bearer not.a.jwt"    # bad token: expect 401
```

A token with one character of its signature changed also returns `401`. An expired token returns `401`: start the API with `Jwt__AccessTokenExpiryMinutes=1`, login, wait about 90 seconds (1 minute plus 30 seconds of allowed clock skew), then call `/me`.

### 4. Lockout and rate limiting

```bash
for i in 1 2 3 4 5; do
  curl -s -o /dev/null -w "wrong password $i: %{http_code}\n" -X POST $B/login -H "$J" -d '{"email":"'"$E"'","password":"WrongPassword@123"}'
done
curl -s -o /dev/null -w 'correct password after 5 failures: %{http_code}\n' -X POST $B/login -H "$J" -d '{"email":"'"$E"'","password":"'"$P"'"}'
```

Expected: all six return `401`. The account is locked for 15 minutes, even for the correct password. Unlock early with:

```sql
update "Users" set "AccessFailedCount"=0, "LockoutEnd"=null where "Email"='<email>';
```

For the rate limit, start the API without `RateLimiting__Login__PermitLimit` and run the login 6 times within a minute. The 6th returns `429` with a `Retry-After` header. Registration is limited to 3 per 10 minutes per IP.

### 5. Inactive accounts

Run the SQL with `psql -U mohanac -d kredflow_advanced_api`, then login again.

| Scenario | SQL | Expected |
|---|---|---|
| Inactive user | `update "Users" set "IsActive"=false where "Email"='<email>';` | Login `401`, identical to a wrong password |
| Inactive lender | `update "Lenders" set "IsActive"=false where "Email"='<email>';` | Login `401`, identical |
| Token outlives deactivation | Login and keep `$T`, run the lender update above, then call `/me` with `$T` | `401` immediately, not after 15 minutes |

Reset afterwards:

```sql
update "Users"   set "IsActive"=true, "AccessFailedCount"=0, "LockoutEnd"=null where "Email"='<email>';
update "Lenders" set "IsActive"=true where "Email"='<email>';
```

Every failed login above must return the same status and message. A failed registration must not leave a lender or user row behind.

### Clean up test records

Preview, then delete the user before the lender (the foreign key is restrictive):

```bash
psql -U mohanac -d kredflow_advanced_api -c "select \"Id\", \"OwnerName\", \"Email\" from \"Lenders\" where \"OwnerName\" = 'Ravi';"

psql -U mohanac -d kredflow_advanced_api <<'SQL'
BEGIN;
DELETE FROM "Users"   WHERE "LenderId" IN (SELECT "Id" FROM "Lenders" WHERE "OwnerName" = 'Ravi');
DELETE FROM "Lenders" WHERE "OwnerName" = 'Ravi';
COMMIT;
SQL
```

This removes every lender whose owner is `Ravi`. To remove one account, match on `"Email"` instead (stored in lower case). Do not run it against the seeded lenders, because the foreign keys will reject the delete.

## Testing the customer API

### Strategy

| Layer | What it proves | How to run |
|---|---|---|
| Automated API tests (`CustomerApiTests`) | Real HTTP pipeline against in-memory Sqlite: validation, tenant isolation, search and paging, soft deactivation, loan history, outstanding maths | `dotnet test KredFlow.slnx --filter "FullyQualifiedName~CustomerApi"` |
| Manual `curl` against PostgreSQL | The migration, real JSON shapes, and a seeded database with loans and payments | This section |
| [KredFlow.Api.http](src/KredFlow.Api/KredFlow.Api.http) | The same requests in VS Code, with the customer id captured automatically | Run the requests in order |

What to verify, in priority order:

1. **Tenant isolation.** Another lender must get `404` on every customer endpoint, and the list must not include foreign customers. This is the most important check.
2. **Authentication.** Every endpoint returns `401` without a token.
3. **Validation.** Bad name, phone, address and paging values return `400` and nothing is saved.
4. **Soft deactivation.** Deactivating never removes loans, and reactivating restores the customer.
5. **Business rules.** Duplicate phone numbers are accepted, and `lenderId` in a body is ignored.
6. **Outstanding.** Only `Active` and `Overdue` loans count, and payments reduce the total.

### Setup

Start the API as in [Testing authentication manually](#setup), then in a second terminal:

```bash
B=http://localhost:5208/api/v1
J='Content-Type: application/json'
P='StrongPassword@123'

# Register a lender, log in, and print its token
token() {
  local email="$1"
  curl -s -o /dev/null -X POST $B/auth/register -H "$J" \
    -d '{"businessName":"ABC","ownerName":"Ravi","email":"'"$email"'","phoneNumber":"9876543210","password":"'"$P"'"}'
  curl -s -X POST $B/auth/login -H "$J" -d '{"email":"'"$email"'","password":"'"$P"'"}' \
    | sed -E 's/.*"accessToken":"([^"]+)".*/\1/'
}

TA=$(token "a$RANDOM@example.com")    # lender A
TB=$(token "b$RANDOM@example.com")    # lender B, used to prove isolation
```

`token` is a shell function, so define it again in each new terminal. If `$TA` is empty, the API is not reachable or the login rate limit was hit; see the note in the auth setup.

### 1. Create

```bash
R=$(curl -s -X POST $B/customers -H "Authorization: Bearer $TA" -H "$J" \
  -d '{"fullName":"  Meena Devi  ","phoneNumber":"9123456789","address":"Chennai"}'); echo $R
C=$(echo $R | sed -E 's/.*"id":"([^"]+)".*/\1/')
```

Expected: `201`, `fullName` trimmed to `Meena Devi`, `phoneNumber` `+919123456789`, `isActive` `true`. Add `-i` to see the `Location` header.

| Scenario | Request body | Expected |
|---|---|---|
| Duplicate phone | Same `phoneNumber`, name `Lakshmi Devi` | `201` (phones are not unique) |
| Body `lenderId` ignored | Add `"lenderId":"00000000-0000-0000-0000-000000000000"` | `201`, owned by the caller's lender |
| Empty name | `"fullName":""` | `400`, `errors.fullName` |
| Bad phone | `"phoneNumber":"12345"` | `400`, `errors.phoneNumber` |
| Name too long | `fullName` of 201 characters | `400` |
| Address too long | `address` of 501 characters | `400` |
| No token | Omit the `Authorization` header | `401` |

### 2. Read and update

```bash
curl -s -w ' [%{http_code}]\n' $B/customers/$C -H "Authorization: Bearer $TA"          # expect 200
curl -s -w ' [%{http_code}]\n' $B/customers/$(uuidgen) -H "Authorization: Bearer $TA"  # unknown id: expect 404
curl -s -w ' [%{http_code}]\n' -X PUT $B/customers/$C -H "Authorization: Bearer $TA" -H "$J" \
  -d '{"fullName":"Meena Sundaram","phoneNumber":"+919000000001","address":null}'      # expect 200, address null
```

### 3. Tenant isolation

Every command below uses lender B's token against lender A's customer, and must return `404`:

```bash
for r in "GET $C" "GET $C/loans" "GET $C/outstanding"; do
  set -- $r; curl -s -o /dev/null -w "$1 $2 [%{http_code}]\n" -X $1 $B/customers/$2 -H "Authorization: Bearer $TB"
done
curl -s -o /dev/null -w 'PUT [%{http_code}]\n' -X PUT $B/customers/$C -H "Authorization: Bearer $TB" -H "$J" \
  -d '{"fullName":"Hacked","phoneNumber":"9000000000"}'
curl -s -o /dev/null -w 'PATCH [%{http_code}]\n' -X PATCH $B/customers/$C/status -H "Authorization: Bearer $TB" -H "$J" \
  -d '{"isActive":false}'
curl -s $B/customers -H "Authorization: Bearer $TB"      # expect "items":[] and "totalCount":0
```

Then read the customer as lender A again: `fullName` must still be `Meena Sundaram` and `isActive` must be `true`.

### 4. List, search and paging

Create a few customers first (names `Anita Rao`, `Bala Murugan`, `Chitra Anand`), then:

```bash
curl -s "$B/customers?page=1&pageSize=2"        -H "Authorization: Bearer $TA"   # 2 items, totalPages reflects the total
curl -s "$B/customers?search=murug"             -H "Authorization: Bearer $TA"   # case-insensitive name match
curl -s "$B/customers?search=9000"              -H "Authorization: Bearer $TA"   # phone match
curl -s "$B/customers?isActive=false"           -H "Authorization: Bearer $TA"   # inactive only
curl -s -w ' [%{http_code}]\n' "$B/customers?pageSize=101" -H "Authorization: Bearer $TA"   # expect 400
curl -s -w ' [%{http_code}]\n' "$B/customers?page=0"       -H "Authorization: Bearer $TA"   # expect 400
```

Expected order is by name. A page past the end returns `200` with an empty `items` list.

### 5. Deactivate and reactivate

```bash
curl -s -w ' [%{http_code}]\n' -X PATCH $B/customers/$C/status -H "Authorization: Bearer $TA" -H "$J" -d '{"isActive":false}'   # 204
curl -s $B/customers/$C -H "Authorization: Bearer $TA"                                                                          # isActive false
curl -s -w ' [%{http_code}]\n' -X PATCH $B/customers/$C/status -H "Authorization: Bearer $TA" -H "$J" -d '{"isActive":true}'    # 204
```

The customer is never deleted, and repeating a request returns `204` again.

### 6. Loans and outstanding with seeded data

New customers have no loans, so `/loans` returns `[]` and `/outstanding` returns `0`. To see real data, use a seeded lender (Development only; see [Development seed data](#development-seed-data)):

```bash
TS=$(curl -s -X POST $B/auth/login -H "$J" \
  -d '{"email":"contact@annapoorna-finance.example","password":"KredFlow@Dev2026!"}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

# Pick a seeded customer that has an Active or Overdue loan
S=$(psql -U mohanac -d kredflow_advanced_api -Atc "select c.\"Id\" from \"Customers\" c join \"Lenders\" le on le.\"Id\" = c.\"LenderId\" join \"Loans\" l on l.\"CustomerId\" = c.\"Id\" where le.\"BusinessName\" = 'Annapoorna Finance' and l.\"Status\" in ('Active', 'Overdue') limit 1")

curl -s $B/customers/$S/loans       -H "Authorization: Bearer $TS"       # loans, newest startDate first, status as text
curl -s $B/customers/$S/outstanding -H "Authorization: Bearer $TS"       # openLoanCount and totalOutstanding
```

Cross-check the figure in `psql`. It must equal the scheduled amount minus the allocated payments for the customer's `Active` and `Overdue` loans:

```bash
psql -U mohanac -d kredflow_advanced_api -c "
select
  (select coalesce(sum(rs.\"TotalDue\"), 0) from \"RepaymentSchedules\" rs
     join \"Loans\" l on l.\"Id\" = rs.\"LoanId\"
    where l.\"CustomerId\" = '$S' and l.\"Status\" in ('Active', 'Overdue')) as due,
  (select coalesce(sum(pa.\"InterestAmount\" + pa.\"PrincipalAmount\"), 0) from \"PaymentAllocations\" pa
     join \"RepaymentSchedules\" rs on rs.\"Id\" = pa.\"ScheduleId\"
     join \"Loans\" l on l.\"Id\" = rs.\"LoanId\"
    where l.\"CustomerId\" = '$S' and l.\"Status\" in ('Active', 'Overdue')) as paid;"
```

`totalOutstanding` is `due - paid`. Closed and defaulted loans must not contribute. A customer whose first loan is `Defaulted` or `Closed` correctly shows `0`, which is why the query above picks a customer with an open loan.

### Clean up

Customers created above belong to the throwaway lenders, so removing them is part of the [auth cleanup](#clean-up-test-records) (delete customers first because of the restrictive foreign key):

```bash
psql -U mohanac -d kredflow_advanced_api -c "delete from \"Customers\" where \"LenderId\" in (select \"Id\" from \"Lenders\" where \"OwnerName\" = 'Ravi');"
```

Run it before the lender delete in the auth cleanup, and never against the seeded lenders.

## Tests

```bash
dotnet test KredFlow.slnx
```

Run only the auth tests with `dotnet test KredFlow.slnx --filter "FullyQualifiedName~Auth"`, or only the customer tests with `--filter "FullyQualifiedName~CustomerApi"`.

- `DatabaseSeederTests` covers the seed data and its determinism.
- `AuthApiTests` runs the real API over HTTP against in-memory Sqlite: registration, duplicate emails, validation, login claims, uniform `401` failures, lockout, `me`, and rejection of tokens after a lender is deactivated.
- `AuthEdgeCaseTests` covers rate limiting, startup with a missing signing key or invalid time zone list, transaction rollback, phone normalization and the password policy.
- `CustomerApiTests` covers the customer API: create, validation, tenant isolation, search and paging, soft deactivation, loan history and outstanding.
- `LenderApiTests` covers the lender profile: read, update, owner-only access, and deactivation (including blocked access afterwards, kept data, and the open-loan rule).

Integration tests run with the `Testing` environment, which skips the Development migrate-and-seed step. The tests create the schema with `EnsureCreated`, because the migrations are specific to PostgreSQL.

## Roadmap

- Loan, Payment and Reporting endpoints, with all queries scoped to the caller's `lenderId`
- Refresh tokens (hashed at rest, rotated on use) and logout
- Staff user management for a lender
- Business-date calculations in the lender's time zone
