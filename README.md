# Deutchezz Hotel — Backend API

ASP.NET Core 9 Web API for a hotel booking and front-desk system. It powers guest bookings, a reception console for check-in/check-out and payments, and an admin console for rooms, pricing, staff, and reports.

> Personal portfolio project by Simon Agboji, using sample data.

**Frontend repo:** [Deutchezz-Hotel/Deutchezz-Frontend](https://github.com/Deutchezz-Hotel/Deutchezz-Frontend)

[![.NET](https://github.com/Deutchezz-Hotel/Deutchezz-Backend/actions/workflows/dotnet.yml/badge.svg)](https://github.com/Deutchezz-Hotel/Deutchezz-Backend/actions/workflows/dotnet.yml)

---

## Contents

- [Features](#features)
- [Tech stack](#tech-stack)
- [Architecture](#architecture)
- [API overview](#api-overview)
- [Getting started](#getting-started)
- [Demo accounts](#demo-accounts)
- [Configuration](#configuration)
- [Email and image uploads](#email-and-image-uploads)
- [Testing and CI](#testing-and-ci)
- [Troubleshooting](#troubleshooting)

## Features

- **Booking engine** — availability search, price quotes with surcharge tiers, multi-room bookings, booking references, guest lookup and cancellation, duplicate-booking warnings, and cancellation windows.
- **Three roles** — `Customer`, `Receptionist`, and `Administrator`, enforced with ASP.NET Core Identity, JWT access/refresh tokens, and authorization policies.
- **Secure sign-up** — email OTP verification and password reset. Codes are HMAC-hashed, with resend cooldowns, hourly send limits, and failed-attempt lockout.
- **Reception console** — walk-in bookings, room assignment, check-in/check-out, early and late check-out rules, and payment recording (cash, POS, bank transfer, online).
- **Admin console** — rooms and room types (with Cloudinary images), settings and stay policy, staff and users, guests, payments, activity log, notifications, and reports.
- **Background jobs** — expire unpaid pending bookings, clean up stale registrations, send check-out reminders, and purge old cancelled bookings.
- **Transactional email** — templated booking, payment, verification, and staff-alert emails over SMTP (MailKit). Without credentials, emails are logged instead of sent.
- **Hardening** — per-endpoint rate limiting, strict CORS, startup validation of all settings, and database conflict detection.

## Tech stack

| Area | Technology |
|---|---|
| Runtime | .NET 9, ASP.NET Core Web API, C# |
| Data | PostgreSQL 16, Entity Framework Core 9 (Npgsql) |
| Auth | ASP.NET Core Identity, JWT bearer, refresh tokens, OTP |
| Email | MailKit (SMTP) |
| Media | Cloudinary |
| API docs | OpenAPI + Scalar |
| Testing | xUnit, `WebApplicationFactory` integration tests, NetArchTest |
| DevOps | Docker Compose (PostgreSQL), GitHub Actions CI |

## Architecture

Clean-architecture layering inside one application project (`src/Deutchezz.csproj`). The dependency direction is enforced by architecture tests.

```
src/
├── Domain/          Entities, enums, constants
├── Application/     Services, DTOs, booking rules, email templates, background workers
├── Infrastructure/  EF Core, migrations, seeders, SMTP, Cloudinary, token/OTP services
└── Api/             Controllers, OpenAPI configuration, Program.cs
tests/
├── Deutchezz.UnitTests/          Booking rules, stays, emails, contact
├── Deutchezz.IntegrationTests/   Full HTTP flows against a real PostgreSQL database
└── Deutchezz.ArchitectureTests/  Layer-dependency rules
```

## API overview

All routes are under `/api/v1`.

| Area | Route | Access |
|---|---|---|
| Auth | `/api/v1/auth` | Public / signed in |
| Room types & pricing | `/api/v1/room-types`, `/api/v1/pricing/quote` | Public |
| Availability & stay policy | `/api/v1/availability/search`, `/api/v1/stay-policy` | Public |
| Bookings | `/api/v1/bookings` (create), `/lookup`, `/cancel` | Public |
| Contact | `/api/v1/contact` | Public |
| My account | `/api/v1/me` | Customer |
| Reception | `/api/v1/reception` | Receptionist, Administrator |
| Admin | `/api/v1/admin` | Administrator |

In Development, the interactive API reference is served at `/scalar/v1` and the OpenAPI document at `/openapi/v1.json`.

## Getting started

### Prerequisites

- .NET SDK 9 (the exact version is pinned in `global.json`)
- Docker Desktop, or a local PostgreSQL 16 install
- Git

### 1. Clone

```powershell
git clone https://github.com/Deutchezz-Hotel/Deutchezz-Backend.git
Set-Location .\Deutchezz-Backend
```

### 2. Start PostgreSQL

```powershell
docker compose up -d
```

This starts PostgreSQL 16 on `localhost:5432` with a `deutchezz` database.

<details>
<summary>Using a local PostgreSQL install instead of Docker</summary>

```powershell
psql -U postgres -c "CREATE USER postgres_dev WITH PASSWORD 'postgres_dev_password';"
psql -U postgres -c "CREATE DATABASE deutchezz OWNER postgres_dev;"
```

Then use this connection string:
`Host=localhost;Port=5432;Database=deutchezz;Username=postgres_dev;Password=postgres_dev_password`

</details>

### 3. Create the Development config

```powershell
Copy-Item .\src\appsettings.Development.json.example .\src\appsettings.Development.json
```

Open the new file and:

1. Replace the masked database password (`******`) with the Docker Compose one:

   ```text
   Host=localhost;Port=5432;Database=deutchezz;Username=postgres;Password=postgres_dev_password
   ```

2. Replace the `REPLACE_ME_…@example.com` seed emails (and passwords, if you like) under `Seed`. Each password must have at least 8 characters, including an uppercase letter, a lowercase letter, and a digit.

`appsettings.Development.json` is git-ignored. The example already contains safe development values for JWT/OTP keys, CORS, and the frontend URL. SMTP and Cloudinary values are placeholders, needed only for those services.

### 4. Run

```powershell
dotnet run --project .\src --launch-profile http
```

| Profile | URLs |
|---|---|
| `http` | `http://localhost:5144` |
| `https` | `https://localhost:7041` (also binds `http://localhost:5144`) |

On startup outside Production, the API applies migrations and seeds roles and the admin account. In Development it also seeds three room types with surcharge tiers, a 20-room sample inventory, the demo receptionist and customer, and four sample bookings for the customer. Existing records are never replaced, and Production never creates the schema on startup.

### 5. Connect the frontend

In the frontend's `.env`, set:

```text
VITE_API_URL=http://localhost:5144/api/v1
```

`http://localhost:5173` and `http://localhost:3000` are allowed by default in `Cors:AllowedOrigins`.

## Demo accounts

Each account is created only when both its email and password are set in `src/appsettings.Development.json`. Development only; never use these in production.

| Role | Email | Password | Demo name |
|---|---|---|---|
| Administrator | `Seed:AdminEmail` | `Seed:AdminPassword` | — |
| Receptionist | `Seed:ReceptionistEmail` | `Seed:ReceptionistPassword` | Peter Receptionist |
| Customer | `Seed:CustomerEmail` | `Seed:CustomerPassword` | Simon Fidelity |

## Configuration

Settings are validated at startup, so the API refuses to start if a required value is missing or out of range. The main required keys are:

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection |
| `Jwt:Key` (≥ 32 bytes), `Jwt:Issuer`, `Jwt:Audience` | Token signing and validation |
| `Security:OtpHmacKey` (≥ 32 bytes) | OTP hashing |
| `Cors:AllowedOrigins` | Exact frontend origins |
| `Email:FrontendBaseUrl` | Absolute frontend URL used in email links |

<details>
<summary>Full configuration reference</summary>

| Key | Purpose | Required to start? |
|---|---|---|
| `Logging:LogLevel:*` | Log levels | No |
| `AllowedHosts` | Host filtering | No |
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string | **Yes** |
| `Jwt:Issuer` / `Jwt:Audience` | JWT claims and validation | **Yes** |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | Token lifetimes | **Yes** (> 0) |
| `Jwt:Key` | JWT signing key | **Yes** (≥ 32 bytes) |
| `Seed:AdminEmail` / `Seed:AdminPassword` | Development admin | No; skipped unless both are set |
| `Seed:ReceptionistEmail` / `Seed:ReceptionistPassword` | Development receptionist | No; skipped unless both are set |
| `Seed:CustomerEmail` / `Seed:CustomerPassword` | Development customer and sample bookings | No; skipped unless both are set |
| `Cancellation:WindowHours` | Cancellation window | **Yes** (> 0) |
| `Booking:DuplicateWarningMinutes` | Duplicate-booking warning window | No |
| `Settings:MaxRoomsPerBooking` | Maximum rooms per booking | **Yes** (1–1000) |
| `Settings:CheckInTime` / `Settings:CheckOutTime` | Default check-in/out times | **Yes** |
| `Settings:RequireFullPaymentAtCheckIn` | Payment gate at check-in | No |
| `Settings:RequirePaymentBeforeCheckOut` | Payment gate at check-out | No |
| `Settings:EarlyCheckOutRequiresAdmin` | Early check-out needs admin | No |
| `Settings:Default*/Earliest*/Latest*Time`, `TimeSlotMinutes`, `TurnaroundMinutes` | Stay-policy timing | No |
| `Settings:MaxStayNights` | Maximum stay length | **Yes** (1–365) |
| `Settings:GraceHours` | Free late check-out grace | **Yes** (0–24) |
| `Settings:CheckOutReminderLeadMinutes` | Reminder lead time | **Yes** (5–2880) |
| `Settings:ReceptionNotificationEmails` | Staff inboxes for reminders | No |
| `Jobs:*IntervalMinutes` | Background job intervals | **Yes** (> 0) |
| `BookingRetention:Enabled` / `CancelledDays` / `IntervalMinutes` | Cancelled-booking purge | Days and interval required |
| `Auth:CodeMinutes`, `ResendCooldownSeconds`, `MaxSendsPerHour`, `PendingRegistrationHours`, `MaxFailedAttempts` | OTP rules | **Yes** |
| `Security:OtpHmacKey` | OTP HMAC key | **Yes** (≥ 32 bytes) |
| `Cors:AllowedOrigins` | Allowed browser origins | **Yes** (≥ 1 absolute URL) |
| `RateLimiting:{Auth,Otp,Lookup,BookingCreate,Contact}:*` | Request limit and window per policy | **Yes** (positive values) |
| `Email:Smtp:Host/Port/Username/Password/FromAddress/FromName` | SMTP delivery | Port must be valid; the rest only for real delivery |
| `Email:AdminNotificationAddress` | Staff alert mailbox | No |
| `Email:FrontendBaseUrl` | Frontend URL in email links | **Yes** (absolute URL) |
| `Cloudinary:CloudName/ApiKey/ApiSecret` | Room-type image uploads | Only for uploads |

</details>

> Never commit real SMTP, JWT, OTP, or Cloudinary secrets. Use the git-ignored Development file or `dotnet user-secrets`.

## Email and image uploads

- **SMTP:** without credentials, the API uses `LoggingEmailSender`, which logs emails instead of sending them. Registration verification and password reset therefore can't be completed until SMTP is configured under `Email:Smtp:*`.
- **Cloudinary:** placeholder credentials don't block startup, but image uploads will fail until real `Cloudinary:*` values are set.

## Testing and CI

```powershell
dotnet test
```

| Project | What it covers |
|---|---|
| `Deutchezz.UnitTests` | Booking guards, stay rules, email templates, contact |
| `Deutchezz.IntegrationTests` | Auth, availability, bookings, pricing, payments, reception and walk-in flows, admin, policies |
| `Deutchezz.ArchitectureTests` | Layer-dependency rules |

Integration tests always use a separate `deutchezz_test` database, so your development data is never touched. Create it once (for example, `createdb deutchezz_test`, or via pgAdmin).

The test host does **not** read `src/appsettings*.json`. It takes its connection from the `DEUTCHEZZ_TEST_CONNECTION` environment variable, or the `ConnectionStrings:DefaultConnection` user secret of the integration-test project. If neither is set, it uses `postgres` / `postgres` on `localhost:5432`. With the Docker Compose database:

```powershell
$env:DEUTCHEZZ_TEST_CONNECTION = "Host=localhost;Port=5432;Username=postgres;Password=postgres_dev_password"
dotnet test
```

**CI:** GitHub Actions (`.github/workflows/dotnet.yml`) restores, builds, and runs the full test suite against a PostgreSQL 16 service on every push and pull request to `main`.

## Troubleshooting

| Problem | Fix |
|---|---|
| Port 5432 already in use | `Get-NetTCPConnection -LocalPort 5432 -State Listen` to find the process, or change the host port in `docker-compose.yml` and the connection string |
| Migration / permission errors | Check `docker compose ps` and `docker compose logs db`; make sure the connection string matches |
| "Key too short" on startup | `Jwt:Key` and `Security:OtpHmacKey` must each be at least 32 bytes |
| CORS error in the browser | Add the exact frontend origin (scheme + host + port) to `Cors:AllowedOrigins` and restart. `localhost` and `127.0.0.1` count as different origins |
| Reset the dev database | `docker compose down -v` then `docker compose up -d`, then run the API again to re-seed |

## Author

**Simon Agboji** — Software Engineer (C# · ASP.NET Core)
[Portfolio](https://simon-a.netlify.app) · [GitHub](https://github.com/agbojisimon) · [LinkedIn](https://www.linkedin.com/in/agboji-simon)
