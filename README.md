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
| Loan management API (`/loans`): create with schedule, read, list, schedule, outstanding, close, prepayment | Implemented |
| Payment (regular payments, reversals) and Reporting APIs | Not started |
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
| `Loan` | Principal, annual interest rate, tenure, EMI, start date and status (`Active`, `Closed`, `Overdue`, `Defaulted`). Also `PrepaymentPolicy` (`ReduceTenure`, `ReduceEmi`, `LenderChoice`), `CreatedAt`, `ClosedDate`, `ClosureRemarks` and a concurrency `Version`. `TenureMonths` and `EmiAmount` hold the current values, so a prepayment recast updates them. |
| `RepaymentSchedule` | One row per monthly installment (opening principal, principal, interest, total due, due date). A prepayment recast marks replaced rows `IsSuperseded` instead of deleting them; only current rows are shown and counted. Paid and remaining amounts are never stored: they are summed from allocations. |
| `Payment` | Money received for a loan (amount, date, method: `Cash`, `UPI`, `BankTransfer`, ...), a `Type` (`Regular`, `Prepayment`), `Remarks` and `CreatedAt`. |
| `PaymentAllocation` | How a payment is split between interest and principal. Either applied to a schedule row (`Installment`) or principal paid ahead of the schedule (`PrincipalPrepayment`, no schedule row). |
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
| `Loans:MinPrincipal` / `MaxPrincipal` | `1000` / `10000000` | Allowed principal for a new loan. |
| `Loans:MinAnnualInterestRate` / `MaxAnnualInterestRate` | `0.01` / `60` | Allowed annual interest rate in percent (at most 2 decimals). |
| `Loans:MinTenureMonths` / `MaxTenureMonths` | `1` / `360` | Allowed tenure. |
| `Loans:MaxBackdatedDays` / `MaxFutureDays` | `365` / `30` | How far from the lender's today a loan's start date may be. |
| `Loans:DefaultPrepaymentPolicy` | `LenderChoice` | Policy given to a loan created without one. The app fails to start if a minimum exceeds its maximum. |
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
- Rejecting new loans for an inactive customer is enforced by loan creation (`422`); reactivating the customer allows it again.

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
- Per loan it uses the same definition as [loan outstanding](#loan-outstanding): unpaid principal plus interest accrued on installments already due, as of today in the lender's time zone. Future interest is not included.
- A loan with no repayment schedule yet counts its full principal.
- A customer with no open loans returns `0` for both numbers.

### Errors

| Status | When |
|---|---|
| `400` | Blank or too long `fullName`, invalid `phoneNumber`, `address` over 500 characters, or an invalid list query. The body is a validation problem with an `errors` object keyed by field |
| `401` | Missing, invalid or expired token |
| `404` | The id does not exist **or belongs to another lender**. Both cases are deliberately identical, so ids cannot be probed across lenders |

## Loan API

Loans belong to the caller's lender (taken from the token, never from the request). A loan of another lender is reported as `404`, the same as an unknown id. All endpoints require a valid token; closing a loan needs the `Owner` role.

| Method and path | Role | Success | Failure |
|---|---|---|---|
| `POST /api/v1/loans` | Any | `201 Created` with `Location` | `400`, `401`, `404`, `422` |
| `GET /api/v1/loans/{loanId}` | Any | `200 OK` | `401`, `404` |
| `GET /api/v1/loans?customerId=&status=&minPrincipal=&maxPrincipal=&startDateFrom=&startDateTo=&page=&pageSize=` | Any | `200 OK` (`PagedResponse`) | `400`, `401` |
| `GET /api/v1/loans/{loanId}/schedule?asOfDate=` | Any | `200 OK` | `401`, `404` |
| `GET /api/v1/loans/{loanId}/outstanding?asOfDate=` | Any | `200 OK` | `401`, `404` |
| `POST /api/v1/loans/{loanId}/close` | `Owner` | `200 OK` | `400`, `401`, `403`, `404`, `409`, `422` |
| `POST /api/v1/loans/{loanId}/prepayments` | Any | `201 Created` | `400`, `401`, `404`, `409`, `422` |

### Create a loan

```json
{
  "customerId": "22222222-2222-2222-2222-222222222222",
  "principalAmount": 100000,
  "annualInterestRate": 12,
  "tenureMonths": 12,
  "startDate": "2026-11-15",
  "prepaymentPolicy": "LenderChoice"
}
```

The EMI, the lender and the schedule rows are backend responsibilities. If a client sends `emiAmount`, `lenderId` or schedule rows, they are ignored.

| Field | Rule |
|---|---|
| `customerId` | Must be an **active** customer of the caller's lender (`404` if not found or owned by another lender, `422` if inactive) |
| `principalAmount` | `Loans:MinPrincipal` to `Loans:MaxPrincipal` (default 1,000 to 10,000,000), at most 2 decimals |
| `annualInterestRate` | `Loans:MinAnnualInterestRate` to `Loans:MaxAnnualInterestRate` (default 0.01 to 60), at most 2 decimals |
| `tenureMonths` | `Loans:MinTenureMonths` to `Loans:MaxTenureMonths` (default 1 to 360) |
| `startDate` | Within `Loans:MaxBackdatedDays` before and `Loans:MaxFutureDays` after today in the lender's time zone (default 365 and 30) |
| `prepaymentPolicy` | Optional: `ReduceTenure`, `ReduceEmi` or `LenderChoice`. Defaults to `Loans:DefaultPrepaymentPolicy` |

The service checks the customer, calculates the EMI with the reducing-balance formula, builds the schedule, and saves the loan, schedule rows and a `Disbursement` ledger entry in **one transaction**. If the generated schedule does not match the loan terms, nothing is saved.

```json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "customerId": "22222222-2222-2222-2222-222222222222",
  "principalAmount": 100000,
  "annualInterestRate": 12,
  "tenureMonths": 12,
  "emiAmount": 8884.88,
  "startDate": "2026-11-15",
  "status": "Active",
  "prepaymentPolicy": "LenderChoice",
  "scheduleGenerated": true,
  "closedDate": null,
  "createdAt": "2026-11-12T10:00:00Z"
}
```

**Calculation rules** (the same for every schedule the system generates):

- Monthly rate is `annualInterestRate / 12 / 100`. EMI is `P * r * (1+r)^n / ((1+r)^n - 1)`; a zero rate divides the principal evenly.
- Money is rounded to 2 decimals, half away from zero. Each row's interest is the opening principal times the monthly rate; its principal is the EMI minus that interest. The last installment takes whatever principal is left, so principal always sums exactly to the loan amount.
- Installment `n` is due `startDate` plus `n` months, always counted from the start date, so a loan starting on the 31st is due on the last day of shorter months and then returns to the 31st.

### Get and list

`GET /api/v1/loans/{loanId}` returns the `LoanResponse` above. `GET /api/v1/loans` returns a `PagedResponse<LoanResponse>`, newest `startDate` first.

| Query parameter | Rule |
|---|---|
| `customerId` | Loans of one customer |
| `status` | `Active`, `Closed`, `Defaulted` or `Overdue` |
| `minPrincipal`, `maxPrincipal` | Inclusive; the minimum must not exceed the maximum |
| `startDateFrom`, `startDateTo` | Inclusive `yyyy-MM-dd`; `from` must not be after `to` |
| `page`, `pageSize` | `1` or more, default 20, max 100 |

### Repayment schedule

`GET /api/v1/loans/{loanId}/schedule` returns the current installments in order. `asOfDate` (default: today in the lender's time zone) only affects `paymentStatus`.

```json
[
  {
    "installmentNumber": 1,
    "dueDate": "2026-12-15",
    "openingPrincipal": 100000,
    "principalDue": 7884.88,
    "interestDue": 1000,
    "totalDue": 8884.88,
    "paidAmount": 0,
    "remainingAmount": 8884.88,
    "paymentStatus": "Upcoming"
  }
]
```

`paidAmount` and `remainingAmount` are computed from payment allocations on every request; there is no editable paid figure.

| `paymentStatus` | When |
|---|---|
| `Paid` | Nothing remains |
| `Overdue` | Something remains and the due date is before `asOfDate` |
| `Partial` | Part is paid and the due date is not yet past |
| `Due` | Unpaid and due on `asOfDate` |
| `Upcoming` | Unpaid and due after `asOfDate` |

### Loan outstanding

`GET /api/v1/loans/{loanId}/outstanding?asOfDate=2026-12-15`:

```json
{
  "loanId": "33333333-3333-3333-3333-333333333333",
  "principalOutstanding": 92115.12,
  "accruedUnpaidInterest": 0,
  "overdueAmount": 0,
  "totalOutstanding": 92115.12,
  "asOfDate": "2026-12-15"
}
```

Accrual is **per installment**, not daily:

- `principalOutstanding` is the loan principal minus every principal allocation (installment payments and prepayments).
- `accruedUnpaidInterest` is the unpaid interest of installments due on or before `asOfDate`. Interest of later installments has not accrued, and nothing accrues between due dates.
- `overdueAmount` is the unpaid total (principal and interest) of installments due **before** `asOfDate`.
- `totalOutstanding` is `principalOutstanding + accruedUnpaidInterest`. It is not what a customer must pay today: use `overdueAmount` for arrears.

### Close a loan

`POST /api/v1/loans/{loanId}/close` (Owner only):

```json
{ "closureDate": "2027-11-15", "remarks": "All dues settled" }
```

Returns `200` with the `LoanResponse` (`status` `Closed`, `closedDate` set). The loan must have no principal, no accrued interest as of `closureDate`, and no net penalties (penalty entries minus waivers). Otherwise `422` lists the amounts still due.

| Rule | Result |
|---|---|
| Loan already closed | `409` |
| `closureDate` in the future, before the start date, or before the last payment | `422` |
| Penalties not waived | `422`. There is no way to pay a penalty yet, so a penalized loan can only be closed after a waiver entry exists |
| Not an `Owner` | `403` |

### Prepayment

`POST /api/v1/loans/{loanId}/prepayments`:

```json
{
  "amount": 20000,
  "paymentDate": "2027-02-15",
  "treatment": "ReduceTenure",
  "paymentMethod": "BankTransfer",
  "remarks": "Additional principal repayment"
}
```

A prepayment is posted as a `Payment` of type `Prepayment`, with its allocations and a `Repayment` ledger entry. `paymentMethod` defaults to `Cash`. The response (`201`) shows what happened:

```json
{
  "paymentId": "b6a8...",
  "loanId": "3333...",
  "amount": 20000,
  "paymentDate": "2027-02-15",
  "treatment": "ReduceTenure",
  "installmentAmountSettled": 0,
  "principalPrepaid": 20000,
  "principalOutstanding": 80000,
  "emiAmount": 8884.88,
  "tenureMonths": 10
}
```

**Prepayment policy** is set per loan at creation:

| Policy | Effect of a prepayment |
|---|---|
| `ReduceTenure` | The EMI stays; the remaining installments are regenerated and finish sooner |
| `ReduceEmi` | The remaining installment count stays; the EMI is recalculated on the lower balance |
| `LenderChoice` (default) | Each request must say `treatment`: `ReduceTenure` or `ReduceEmi` (`400` if missing) |

For the two fixed policies `treatment` may be omitted; a different value returns `422`.

**How it is posted:**

1. Every installment due on or before `paymentDate` must already be fully paid, otherwise `422` (a prepayment never hides arrears).
2. If a future installment is partly paid, the amount settles it first (interest, then principal).
3. The rest is recorded as principal paid ahead of the schedule, as an allocation with no schedule row.
4. Installments after the last due or part-paid one are marked superseded and regenerated on the new balance, continuing the numbering. Interest is recomputed on the lower balance from the next unpaid installment, so there is no daily or pro-rata interest. Superseded rows are kept for audit.
5. The loan's `emiAmount`, `tenureMonths` and concurrency version are updated, all in one transaction.

| Rule | Result |
|---|---|
| `amount` is not positive | `400` |
| `amount` is more than the payoff (principal outstanding plus unpaid interest of part-paid installments) | `422` |
| `paymentDate` in the future, before the loan start, or before the last payment | `422` |
| Loan closed | `422` |
| A concurrent change to the same loan | `409`; reload and retry |

Paying off the entire balance leaves no installments; the loan stays `Active` until it is closed explicitly.

### Errors

| Status | When |
|---|---|
| `400` | Invalid request (see the rules above). The body has an `errors` object keyed by field |
| `401` | Missing, invalid or expired token |
| `403` | Closing a loan without the `Owner` role |
| `404` | Loan or customer not found, **or owned by another lender** (deliberately identical) |
| `409` | Loan already closed, or the loan was changed by another request |
| `422` | Inactive customer, outstanding dues, penalties, or an invalid prepayment |

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

Cross-check the figure in `psql`. It must equal unpaid principal plus the unpaid interest of installments already due, over the customer's `Active` and `Overdue` loans:

```bash
psql -U mohanac -d kredflow_advanced_api -c "
select
  (select coalesce(sum(l.\"Principal\"), 0) from \"Loans\" l
    where l.\"CustomerId\" = '$S' and l.\"Status\" in ('Active', 'Overdue'))
  - (select coalesce(sum(pa.\"PrincipalAmount\"), 0) from \"PaymentAllocations\" pa
       join \"Payments\" p on p.\"Id\" = pa.\"PaymentId\"
       join \"Loans\" l on l.\"Id\" = p.\"LoanId\"
      where l.\"CustomerId\" = '$S' and l.\"Status\" in ('Active', 'Overdue')) as principal_outstanding,
  (select coalesce(sum(rs.\"InterestDue\"), 0) from \"RepaymentSchedules\" rs
     join \"Loans\" l on l.\"Id\" = rs.\"LoanId\"
    where l.\"CustomerId\" = '$S' and l.\"Status\" in ('Active', 'Overdue')
      and not rs.\"IsSuperseded\" and rs.\"DueDate\" <= current_date)
  - (select coalesce(sum(pa.\"InterestAmount\"), 0) from \"PaymentAllocations\" pa
       join \"RepaymentSchedules\" rs on rs.\"Id\" = pa.\"ScheduleId\"
       join \"Loans\" l on l.\"Id\" = rs.\"LoanId\"
      where l.\"CustomerId\" = '$S' and l.\"Status\" in ('Active', 'Overdue')
        and not rs.\"IsSuperseded\" and rs.\"DueDate\" <= current_date) as accrued_interest;"
```

`totalOutstanding` is `principal_outstanding + accrued_interest`. Closed and defaulted loans must not contribute. A customer whose loans are all `Defaulted` or `Closed` correctly shows `0`, which is why the query above picks a customer with an open loan.

### Clean up

Customers created above belong to the throwaway lenders, so removing them is part of the [auth cleanup](#clean-up-test-records) (delete customers first because of the restrictive foreign key):

```bash
psql -U mohanac -d kredflow_advanced_api -c "delete from \"Customers\" where \"LenderId\" in (select \"Id\" from \"Lenders\" where \"OwnerName\" = 'Ravi');"
```

Run it before the lender delete in the auth cleanup, and never against the seeded lenders.

## Testing the loan API

### Strategy

| Layer | What it proves | How to run |
|---|---|---|
| Unit tests (`LoanCalculatorTests`) | EMI and schedule maths, rounding, month-end due dates, prepayment allocation, balance and accrual rules | `dotnet test KredFlow.slnx --filter "FullyQualifiedName~LoanCalculator"` |
| API tests (`LoanApiTests`) | The real HTTP pipeline on in-memory Sqlite: create with schedule, validation, tenant isolation, filters, derived statuses, close rules, both prepayment treatments | `dotnet test KredFlow.slnx --filter "FullyQualifiedName~LoanApi"` |
| Manual `curl` against PostgreSQL | The migration, the partial unique index that lets a recast reuse installment numbers, real JSON shapes and seeded data | This section |

What to verify, in priority order:

1. **Money is right.** The EMI for 100,000 at 12% over 12 months is `8884.88`, the first row splits `1000` interest and `7884.88` principal, and principal across all rows sums to exactly the loan amount.
2. **Tenant isolation.** Another lender gets `404` on every loan endpoint, and cannot create a loan for your customer.
3. **Backend-owned fields.** A client-sent `emiAmount`, `lenderId` or schedule is ignored.
4. **Atomicity.** A rejected request leaves no loan, schedule, payment or ledger rows behind.
5. **Derived figures.** Paid, remaining, status and outstanding change only when allocations change.
6. **Rules.** Close and prepayment reject what the [Loan API](#loan-api) says they reject.

### Setup

Reuse the `token` function and `$TA` / `$TB` tokens from [Testing the customer API](#setup-1), then:

```bash
B=http://localhost:5208/api/v1
J='Content-Type: application/json'
TODAY=$(date +%F)
BACK=$(date -v-95d +%F 2>/dev/null || date -d '-95 days' +%F)      # 95 days ago (macOS or Linux)

C=$(curl -s -X POST $B/customers -H "Authorization: Bearer $TA" -H "$J" \
  -d '{"fullName":"Meena Devi","phoneNumber":"9123456789"}' | sed -E 's/.*"id":"([^"]+)".*/\1/')

# Create a loan and print its id: loan <principal> <rate> <months> <startDate> [policy]
loan() {
  curl -s -X POST $B/loans -H "Authorization: Bearer $TA" -H "$J" \
    -d '{"customerId":"'$C'","principalAmount":'$1',"annualInterestRate":'$2',"tenureMonths":'$3',"startDate":"'$4'"'"${5:+,\"prepaymentPolicy\":\"$5\"}"'}' \
    | sed -E 's/.*"id":"([^"]+)".*/\1/'
}

# Stand-in for the Payment module: record a full payment of one installment
pay() {
  psql -U mohanac -d kredflow_advanced_api -c "
  with r as (select \"Id\", \"LoanId\", \"PrincipalDue\", \"InterestDue\", \"TotalDue\" from \"RepaymentSchedules\"
             where \"LoanId\" = '$1' and \"InstallmentNumber\" = $2 and not \"IsSuperseded\"),
       p as (insert into \"Payments\"(\"Id\", \"LoanId\", \"Amount\", \"PaymentDate\", \"PaymentMethod\", \"Type\", \"CreatedAt\")
             select gen_random_uuid(), \"LoanId\", \"TotalDue\", current_date, 'Cash', 'Regular', now() from r returning \"Id\")
  insert into \"PaymentAllocations\"(\"Id\", \"PaymentId\", \"ScheduleId\", \"InterestAmount\", \"PrincipalAmount\", \"Type\")
  select gen_random_uuid(), p.\"Id\", r.\"Id\", r.\"InterestDue\", r.\"PrincipalDue\", 'Installment' from p, r;"
}
```

The payment endpoints do not exist yet, so `pay` writes a payment and its allocation straight to the database. Define both functions again in each new terminal.

### 1. Create a loan

```bash
curl -s -w ' [%{http_code}]\n' -X POST $B/loans -H "Authorization: Bearer $TA" -H "$J" \
  -d '{"customerId":"'$C'","principalAmount":100000,"annualInterestRate":12,"tenureMonths":12,"startDate":"'$TODAY'","emiAmount":5,"lenderId":"00000000-0000-0000-0000-000000000000"}'
```

Expected: `201`, `emiAmount` `8884.88` (the `5` is ignored), `status` `Active`, `prepaymentPolicy` `LenderChoice`, `scheduleGenerated` `true`. Add `-i` to see the `Location` header.

```bash
L=$(loan 100000 12 12 $TODAY)
curl -s $B/loans/$L/schedule -H "Authorization: Bearer $TA" | python3 -c 'import sys,json; d=json.load(sys.stdin); print(len(d), d[0]); print(round(sum(r["principalDue"] for r in d), 2))'
```

Expected: 12 rows; row 1 has `openingPrincipal` `100000`, `interestDue` `1000`, `principalDue` `7884.88`, `totalDue` `8884.88`, `paymentStatus` `Upcoming`; the principal sum is `100000.0`. Check that the ledger has one disbursement:

```bash
psql -U mohanac -d kredflow_advanced_api -c "select \"TransactionType\", \"Amount\" from \"LoanTransactions\" where \"LoanId\" = '$L';"
```

### 2. Validation and rules

Each of these returns no loan. Change one field of the request in step 1:

| Change | Expected |
|---|---|
| `principalAmount` 999 or 10000001 | `400`, `errors.principalAmount` |
| `annualInterestRate` 0, 60.01 or 12.123 | `400`, `errors.annualInterestRate` |
| `tenureMonths` 0 or 361 | `400`, `errors.tenureMonths` |
| `startDate` 366 days ago or 31 days ahead | `400`, `errors.startDate` |
| `customerId` of a customer of lender B (use `$TB`'s) | `404` |
| `customerId` of a deactivated customer | `422` |
| No `Authorization` header | `401` |

Deactivate and retry to see the inactive rule (reactivate with `true` afterwards):

```bash
curl -s -o /dev/null -X PATCH $B/customers/$C/status -H "Authorization: Bearer $TA" -H "$J" -d '{"isActive":false}'
curl -s -w ' [%{http_code}]\n' -X POST $B/loans -H "Authorization: Bearer $TA" -H "$J" \
  -d '{"customerId":"'$C'","principalAmount":50000,"annualInterestRate":12,"tenureMonths":6,"startDate":"'$TODAY'"}'    # expect 422
curl -s -o /dev/null -X PATCH $B/customers/$C/status -H "Authorization: Bearer $TA" -H "$J" -d '{"isActive":true}'
```

### 3. Tenant isolation

Lender B must get `404` on every endpoint for lender A's loan:

```bash
for r in "GET $L" "GET $L/schedule" "GET $L/outstanding"; do
  set -- $r; curl -s -o /dev/null -w "$1 $2 [%{http_code}]\n" -X $1 $B/loans/$2 -H "Authorization: Bearer $TB"
done
curl -s -o /dev/null -w 'close [%{http_code}]\n' -X POST $B/loans/$L/close -H "Authorization: Bearer $TB" -H "$J" -d '{"closureDate":"'$TODAY'"}'
curl -s -o /dev/null -w 'prepay [%{http_code}]\n' -X POST $B/loans/$L/prepayments -H "Authorization: Bearer $TB" -H "$J" \
  -d '{"amount":5000,"paymentDate":"'$TODAY'","treatment":"ReduceTenure"}'
curl -s $B/loans -H "Authorization: Bearer $TB"        # expect "items":[] and "totalCount":0
```

### 4. List and filter

Create a few loans first (`loan 20000 12 6 $TODAY`, `loan 300000 15 24 $BACK`), then:

```bash
curl -s "$B/loans?status=Active"                              -H "Authorization: Bearer $TA"   # only Active
curl -s "$B/loans?customerId=$C&minPrincipal=50000"           -H "Authorization: Bearer $TA"   # principal >= 50000
curl -s "$B/loans?startDateFrom=$BACK&startDateTo=$TODAY"     -H "Authorization: Bearer $TA"   # by start date
curl -s "$B/loans?page=1&pageSize=2"                          -H "Authorization: Bearer $TA"   # totalPages reflects the total
curl -s -w ' [%{http_code}]\n' "$B/loans?minPrincipal=200&maxPrincipal=100" -H "Authorization: Bearer $TA"   # expect 400
curl -s -w ' [%{http_code}]\n' "$B/loans?status=Bogus"       -H "Authorization: Bearer $TA"   # expect 400
```

Items are newest `startDate` first.

### 5. Overdue, paid and outstanding

Use a loan that started 95 days ago, so three installments are already past due:

```bash
O=$(loan 100000 12 12 $BACK)
curl -s $B/loans/$O/outstanding -H "Authorization: Bearer $TA"
```

Expected: `principalOutstanding` `100000`, `accruedUnpaidInterest` is the interest of the three past rows, `overdueAmount` is 3 x `8884.88` = `26654.64`. In the schedule the first three rows are `Overdue`, the rest `Upcoming`.

Record a payment of installment 1 and look again:

```bash
pay $O 1
curl -s $B/loans/$O/schedule -H "Authorization: Bearer $TA" | python3 -c 'import sys,json; [print(r["installmentNumber"], r["paidAmount"], r["remainingAmount"], r["paymentStatus"]) for r in json.load(sys.stdin)[:4]]'
curl -s $B/loans/$O/outstanding -H "Authorization: Bearer $TA"
```

Expected: row 1 is `Paid` with `remainingAmount` `0`, rows 2 and 3 stay `Overdue`; `principalOutstanding` drops by row 1's principal (`7884.88`); `overdueAmount` drops to 2 x `8884.88`.

`asOfDate` changes what has accrued without changing any data:

```bash
curl -s "$B/loans/$O/outstanding?asOfDate=$BACK" -H "Authorization: Bearer $TA"    # accruedUnpaidInterest 0, overdueAmount 0
```

The customer's outstanding uses the same calculator: `curl -s $B/customers/$C/outstanding -H "Authorization: Bearer $TA"` must equal the sum of `totalOutstanding` over the customer's open loans.

### 6. Prepayment

```bash
P=$(loan 100000 12 12 $TODAY)
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$P/prepayments -H "Authorization: Bearer $TA" -H "$J" \
  -d '{"amount":20000,"paymentDate":"'$TODAY'"}'                                   # LenderChoice without treatment: expect 400
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$P/prepayments -H "Authorization: Bearer $TA" -H "$J" \
  -d '{"amount":20000,"paymentDate":"'$TODAY'","treatment":"ReduceTenure","remarks":"extra"}'
curl -s $B/loans/$P -H "Authorization: Bearer $TA"
curl -s $B/loans/$P/schedule -H "Authorization: Bearer $TA" | python3 -c 'import sys,json; d=json.load(sys.stdin); print(len(d), round(sum(r["principalDue"] for r in d), 2))'
```

Expected: `201`, `treatment` `ReduceTenure`, `principalPrepaid` `20000`, `principalOutstanding` `80000`, `emiAmount` still `8884.88`, `tenureMonths` `10`. The loan shows `tenureMonths` `10`, and the schedule has 10 rows whose principal sums to `80000.0`.

Reduce the EMI instead on a second loan:

```bash
Q=$(loan 100000 12 12 $TODAY)
curl -s -X POST $B/loans/$Q/prepayments -H "Authorization: Bearer $TA" -H "$J" -d '{"amount":20000,"paymentDate":"'$TODAY'","treatment":"ReduceEmi"}'
```

Expected: `tenureMonths` stays `12` and `emiAmount` drops to `7107.9`. Then check the audit trail and the reused numbering:

```bash
psql -U mohanac -d kredflow_advanced_api -c "select \"IsSuperseded\", count(*), min(\"InstallmentNumber\"), max(\"InstallmentNumber\") from \"RepaymentSchedules\" where \"LoanId\" = '$P' group by 1 order by 1;"
```

Expected: 12 superseded rows and 10 current rows, both numbered from 1.

| Scenario | Expected |
|---|---|
| Loan created with `"prepaymentPolicy":"ReduceTenure"`, prepay with `"treatment":"ReduceEmi"` | `422` |
| Same loan, `treatment` omitted | `201`, treatment `ReduceTenure` |
| Prepay the overdue loan `$O` today | `422`, unpaid installments due first |
| `amount` above the payoff (for example `100000.01` on a fresh loan) | `422` |
| `amount` `0` or negative | `400` |
| `paymentDate` tomorrow or before the loan start | `422` |

### 7. Close

```bash
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$P/close -H "Authorization: Bearer $TA" -H "$J" -d '{"closureDate":"'$TODAY'"}'   # dues remain: expect 422
```

The `422` detail lists principal, interest and penalties still due. To close a loan, clear it. A full prepayment is the quickest way, since it needs no due installments:

```bash
F=$(loan 50000 12 6 $TODAY)
curl -s -X POST $B/loans/$F/prepayments -H "Authorization: Bearer $TA" -H "$J" -d '{"amount":50000,"paymentDate":"'$TODAY'","treatment":"ReduceTenure"}'   # principalOutstanding 0, no installments left
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$F/close -H "Authorization: Bearer $TA" -H "$J" -d '{"closureDate":"'$TODAY'","remarks":"All dues settled"}'      # expect 200, status Closed
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$F/close -H "Authorization: Bearer $TA" -H "$J" -d '{"closureDate":"'$TODAY'"}'                              # expect 409
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$F/prepayments -H "Authorization: Bearer $TA" -H "$J" -d '{"amount":1000,"paymentDate":"'$TODAY'","treatment":"ReduceTenure"}'   # closed: expect 422
```

A `Staff` user gets `403` on `close` (the `token` function registers owners, so create a staff user in the database to try it, or rely on `LoanApiTests.Close_IsOwnerOnly`).

### 8. Seeded loans (Development only)

The seeder creates 15 loans spread across the `Closed`, `Active`, `Overdue` and `Defaulted` statuses (see [Development seed data](#development-seed-data)), so you can test existing loans without creating any. Seeded `Closed` loans have a `closedDate` equal to their last payment (databases seeded before this was added show `null`; reset to get it). These helpers pick a loan by status and log in as the lender that owns it. Seeded loans are deterministic, so the ids are the same on every freshly seeded database, but the dates move with today's date, so select by status rather than copying ids.

```bash
B=http://localhost:5208/api/v1
J='Content-Type: application/json'
TODAY=$(date +%F)
PSQL="psql -U mohanac -d kredflow_advanced_api -Atc"

# pick <Status>: id of a seeded loan with that status
pick() { $PSQL "select \"Id\" from \"Loans\" where \"Status\" = '$1' order by \"Id\" limit 1"; }

# tok <loanId>: access token of the seeded owner of that loan's lender (password is public: see Development seed data)
tok() {
  local email; email=$($PSQL "select u.\"Email\" from \"Users\" u join \"Loans\" l on l.\"LenderId\" = u.\"LenderId\" where l.\"Id\" = '$1' limit 1")
  curl -s -X POST $B/auth/login -H "$J" -d '{"email":"'"$email"'","password":"KredFlow@Dev2026!"}' | sed -E 's/.*"accessToken":"([^"]+)".*/\1/'
}
```

Define them again in each new terminal. A `401` from `tok` means the database was seeded before the auth migration; reset it as described in [Reset the local database](#reset-the-local-database).

**A. Closed loan: everything paid, nothing can change**

```bash
CL=$(pick Closed); TC=$(tok $CL)
curl -s $B/loans/$CL -H "Authorization: Bearer $TC"                          # status Closed
curl -s $B/loans/$CL/schedule -H "Authorization: Bearer $TC"                 # every row Paid, remainingAmount 0
curl -s $B/loans/$CL/outstanding -H "Authorization: Bearer $TC"              # principal, interest and overdue all 0
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$CL/close -H "Authorization: Bearer $TC" -H "$J" \
  -d '{"closureDate":"'$TODAY'"}'                                            # expect 409 "already closed"
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$CL/prepayments -H "Authorization: Bearer $TC" -H "$J" \
  -d '{"amount":20000,"paymentDate":"'$TODAY'","treatment":"ReduceTenure"}'  # expect 422 "A closed loan cannot be prepaid"
```

**B. Overdue loan: arrears block prepayment and closing**

```bash
OV=$(pick Overdue); TO=$(tok $OV)
curl -s $B/loans/$OV/outstanding -H "Authorization: Bearer $TO"              # overdueAmount > 0, accruedUnpaidInterest > 0
curl -s $B/loans/$OV/schedule -H "Authorization: Bearer $TO" | python3 -c 'import sys,json; [print(r["installmentNumber"], r["dueDate"], r["paidAmount"], r["remainingAmount"], r["paymentStatus"]) for r in json.load(sys.stdin)]'
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$OV/prepayments -H "Authorization: Bearer $TO" -H "$J" \
  -d '{"amount":5000,"paymentDate":"'$TODAY'","treatment":"ReduceTenure"}'   # expect 422 "Installments due ... must be settled"
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$OV/close -H "Authorization: Bearer $TO" -H "$J" \
  -d '{"closureDate":"'$TODAY'"}'                                            # expect 422 listing principal, interest and penalties
```

The unpaid rows show `Overdue`, and `overdueAmount` equals the sum of their `remainingAmount`. Seeded overdue and defaulted loans also carry `Penalty` ledger entries, so even after the arrears are paid they cannot be closed until a waiver exists.

**C. Defaulted loan**

```bash
DF=$(pick Defaulted); TD=$(tok $DF)
curl -s $B/loans/$DF -H "Authorization: Bearer $TD"                          # status Defaulted
curl -s $B/loans/$DF/outstanding -H "Authorization: Bearer $TD"              # larger overdueAmount than a plain Overdue loan
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$DF/prepayments -H "Authorization: Bearer $TD" -H "$J" \
  -d '{"amount":5000,"paymentDate":"'$TODAY'","treatment":"ReduceEmi"}'     # expect 422, arrears
```

**D. Active loan, on schedule: prepayment works**

```bash
AC=$(pick Active); TA2=$(tok $AC)
curl -s $B/loans/$AC/outstanding -H "Authorization: Bearer $TA2"            # overdueAmount 0, principalOutstanding > 0
curl -s $B/loans/$AC -H "Authorization: Bearer $TA2"                        # note tenureMonths, emiAmount and prepaymentPolicy (LenderChoice)
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$AC/prepayments -H "Authorization: Bearer $TA2" -H "$J" \
  -d '{"amount":20000,"paymentDate":"'$TODAY'"}'                            # expect 400, treatment required
curl -s -w ' [%{http_code}]\n' -X POST $B/loans/$AC/prepayments -H "Authorization: Bearer $TA2" -H "$J" \
  -d '{"amount":20000,"paymentDate":"'$TODAY'","treatment":"ReduceTenure"}' # expect 201
curl -s $B/loans/$AC -H "Authorization: Bearer $TA2"                        # tenureMonths lower, emiAmount unchanged
curl -s $B/loans/$AC/outstanding -H "Authorization: Bearer $TA2"            # principalOutstanding 20000 lower
```

This changes the seeded data, which is fine for a development database. To undo it, reset the database. A seeded loan can also have its last installment due today and unpaid; in that case the prepayment returns `422`, so pick another `Active` loan (`limit 1 offset 1`).

**E. Isolation between the two seeded lenders**

```bash
OTHER=$($PSQL "select u.\"Email\" from \"Users\" u where u.\"LenderId\" <> (select \"LenderId\" from \"Loans\" where \"Id\" = '$CL') limit 1")
TX=$(curl -s -X POST $B/auth/login -H "$J" -d '{"email":"'"$OTHER"'","password":"KredFlow@Dev2026!"}' | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')
curl -s -w ' [%{http_code}]\n' $B/loans/$CL -H "Authorization: Bearer $TX"                # expect 404, not 403
curl -s -w ' [%{http_code}]\n' $B/loans/$CL/schedule -H "Authorization: Bearer $TX"       # expect 404
curl -s "$B/loans?status=Closed&pageSize=100" -H "Authorization: Bearer $TX" | grep -c "$CL"   # expect 0
```

**F. List the seeded portfolio**

```bash
curl -s "$B/loans?status=Overdue" -H "Authorization: Bearer $TO"             # only this lender's overdue loans
curl -s "$B/loans?minPrincipal=500000&pageSize=5" -H "Authorization: Bearer $TO"
curl -s "$B/loans?startDateFrom=2025-01-01&startDateTo=$TODAY&page=1&pageSize=3" -H "Authorization: Bearer $TO"
```

Each lender sees only its own loans, so the totals differ per token.

### Clean up

Loans belong to the throwaway lenders, so extend the [auth cleanup](#clean-up-test-records). Delete in dependency order before the customers and lenders:

```bash
psql -U mohanac -d kredflow_advanced_api <<'SQL'
BEGIN;
DELETE FROM "PaymentAllocations" WHERE "PaymentId" IN (SELECT p."Id" FROM "Payments" p JOIN "Loans" l ON l."Id" = p."LoanId" JOIN "Lenders" le ON le."Id" = l."LenderId" WHERE le."OwnerName" = 'Ravi');
DELETE FROM "Payments" WHERE "LoanId" IN (SELECT l."Id" FROM "Loans" l JOIN "Lenders" le ON le."Id" = l."LenderId" WHERE le."OwnerName" = 'Ravi');
DELETE FROM "LoanTransactions" WHERE "LoanId" IN (SELECT l."Id" FROM "Loans" l JOIN "Lenders" le ON le."Id" = l."LenderId" WHERE le."OwnerName" = 'Ravi');
DELETE FROM "RepaymentSchedules" WHERE "LoanId" IN (SELECT l."Id" FROM "Loans" l JOIN "Lenders" le ON le."Id" = l."LenderId" WHERE le."OwnerName" = 'Ravi');
DELETE FROM "Loans" WHERE "LenderId" IN (SELECT "Id" FROM "Lenders" WHERE "OwnerName" = 'Ravi');
COMMIT;
SQL
```

Then run the customer and auth cleanups. Never run this against the seeded lenders.

## Tests

```bash
dotnet test KredFlow.slnx
```

Run only the auth tests with `dotnet test KredFlow.slnx --filter "FullyQualifiedName~Auth"`, only the customer tests with `--filter "FullyQualifiedName~CustomerApi"`, or only the loan tests with `--filter "FullyQualifiedName~Loan"`.

- `DatabaseSeederTests` covers the seed data and its determinism.
- `AuthApiTests` runs the real API over HTTP against in-memory Sqlite: registration, duplicate emails, validation, login claims, uniform `401` failures, lockout, `me`, and rejection of tokens after a lender is deactivated.
- `AuthEdgeCaseTests` covers rate limiting, startup with a missing signing key or invalid time zone list, transaction rollback, phone normalization and the password policy.
- `CustomerApiTests` covers the customer API: create, validation, tenant isolation, search and paging, soft deactivation, loan history and outstanding.
- `LoanCalculatorTests` covers the pure loan maths: EMI, schedule invariants, month-end due dates, prepayment planning and per-installment accrual.
- `LoanApiTests` covers the loan API: create with schedule and disbursement, ignored client fields, validation, tenant isolation, filters and paging, derived schedule status and outstanding, close rules, and both prepayment treatments.
- `LenderApiTests` covers the lender profile: read, update, owner-only access, and deactivation (including blocked access afterwards, kept data, and the open-loan rule).

Integration tests run with the `Testing` environment, which skips the Development migrate-and-seed step. The tests create the schema with `EnsureCreated`, because the migrations are specific to PostgreSQL.

## Roadmap

- Payment endpoints (regular payments, reversals, partial and excess payments, penalty payment and waiver), reusing the allocation engine from loan prepayments, plus Reporting endpoints, with all queries scoped to the caller's `lenderId`
- A scheduled job that moves loans to `Overdue` and `Defaulted`; until then `status` is whatever was last stored
- Refresh tokens (hashed at rest, rotated on use) and logout
- Staff user management for a lender
- Business-date calculations in the lender's time zone
