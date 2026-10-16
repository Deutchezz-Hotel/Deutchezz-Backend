# Deutchezz Hotel Backend

## What this is

Deutchezz Hotel is an ASP.NET Core booking API targeting .NET 9. It uses PostgreSQL through Entity Framework Core, ASP.NET Core Identity for users and roles, and JWT bearer tokens for authentication. The API exposes controller routes under `/api/...`; OpenAPI and Scalar are mapped in Development.

## Prerequisites

- .NET SDK `9.0.316` or a compatible SDK in the .NET 9 feature band. The required SDK and roll-forward policy are pinned in `global.json`.
- Docker Desktop with Docker Compose, or a local PostgreSQL 16 installation.
- Git.
- Windows PowerShell.

## Setup

Run these commands from the repository root.

### 1. Clone the repository

```powershell
git clone <repository-url>
Set-Location .\<repository-folder-name>
```

The repository URL is `https://github.com/agbojisimon/Deutchezz-Backend.git`.
Replace `<repository-folder-name>` with the folder created by `git clone`.

### 2. Start PostgreSQL

The included `docker-compose.yml` starts PostgreSQL 16 as service `db`, creates database `deutchezz`, and publishes it on port `5432`.

```powershell
docker compose up -d
docker compose ps
```

If Docker is not available, install PostgreSQL 16 locally and create the matching user and database from an administrator PowerShell session. The `psql` commands below use the same development credentials as `docker-compose.yml`:

```powershell
psql -U postgres -c "CREATE USER postgres_dev WITH PASSWORD 'postgres_dev_password';"
psql -U postgres -c "CREATE DATABASE deutchezz OWNER postgres_dev;"
```

For that local alternative, change the example connection string to:

```text
Host=localhost;Port=5432;Database=deutchezz;Username=postgres_dev;Password=postgres_dev_password
```

If the local PostgreSQL installer already provides the `postgres` user, the Docker connection string can be used instead after creating the `deutchezz` database:

```powershell
psql -U postgres -c "CREATE DATABASE deutchezz;"
```

### 3. Create the Development configuration

```powershell
Copy-Item .\src\appsettings.Development.json.example .\src\appsettings.Development.json
```

`src\appsettings.Development.json` is ignored by Git and must not be committed. Open it locally:

```powershell
notepad .\src\appsettings.Development.json
```

The tracked example contains a masked database password (`******`) for safety. Replace it with the Docker Compose password:

```text
Host=localhost;Port=5432;Database=deutchezz;Username=postgres;Password=postgres_dev_password
```

The example already contains safe development values for the JWT and OTP keys, demo seed accounts, CORS origins, SMTP placeholders, Cloudinary placeholders, and frontend URL. Replace placeholders only when the related feature needs a real external service.

### 4. Configuration reference

The following table covers every key in `src\appsettings.json` and `src\appsettings.Development.json.example`. Values in `appsettings.Development.json` override the base settings when `ASPNETCORE_ENVIRONMENT=Development`.

| Key | Purpose | Required to start? |
|---|---|---|
| `Logging:LogLevel:Default` | Default application log level. | No; default is usable. |
| `Logging:LogLevel:Microsoft.AspNetCore` | ASP.NET Core log level. | No; default is usable. |
| `AllowedHosts` | ASP.NET Core host filtering value. | No. |
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string used by EF Core. | **Yes**; it must be nonempty and the database must be reachable because Development startup applies migrations. |
| `Jwt:Issuer` | JWT issuer claim and validation value. | **Yes**; must be nonempty. |
| `Jwt:Audience` | JWT audience claim and validation value. | **Yes**; must be nonempty. |
| `Jwt:AccessTokenMinutes` | Access-token lifetime. | **Yes**; must be greater than zero. |
| `Jwt:RefreshTokenDays` | Refresh-token lifetime. | **Yes**; must be greater than zero. |
| `Jwt:Key` | JWT signing key. | **Yes**; must be at least 32 UTF-8 bytes. |
| `Seed:AdminEmail` | Development administrator email. | Optional as a pair; if set, `Seed:AdminPassword` must also be set. |
| `Seed:AdminPassword` | Development administrator password. | Optional as a pair; required to create the demo administrator. |
| `Seed:ReceptionistEmail` | Development receptionist email. | No; if absent, the receptionist is skipped. |
| `Seed:ReceptionistPassword` | Development receptionist password. | No; if absent, the receptionist is skipped. |
| `Seed:CustomerEmail` | Development customer email. | No; if absent, the customer and demo bookings are skipped. |
| `Seed:CustomerPassword` | Development customer password. | No; if absent, the customer is skipped. |
| `Cancellation:WindowHours` | Cancellation window. | **Yes**; must be greater than zero. |
| `Booking:DuplicateWarningMinutes` | Duplicate-booking warning window. | No; the model default is used if omitted. |
| `Settings:MaxRoomsPerBooking` | Maximum room quantity per booking. | **Yes**; must be 1 through 1000. |
| `Settings:CheckInTime` | Default check-in time. | **Yes**; must parse as a time. |
| `Settings:CheckOutTime` | Default check-out time. | **Yes**; must parse as a time. |
| `Settings:RequireFullPaymentAtCheckIn` | Payment gate at check-in. | No; default is false. |
| `Settings:RequirePaymentBeforeCheckOut` | Payment gate at checkout. | No; default is false. |
| `Settings:EarlyCheckOutRequiresAdmin` | Whether early checkout requires an administrator. | No; model default is used if omitted. |
| `Settings:DefaultCheckInTime` | Stay-policy default check-in time. | No; model default is used if omitted. |
| `Settings:DefaultCheckOutTime` | Stay-policy default check-out time. | No; model default is used if omitted. |
| `Settings:TimeSlotMinutes` | Stay-policy time-slot size. | No; model default is used if omitted. |
| `Settings:EarliestCheckInTime` | Earliest allowed check-in time. | No; model default is used if omitted. |
| `Settings:LatestCheckInTime` | Latest allowed check-in time. | No; model default is used if omitted. |
| `Settings:EarliestCheckOutTime` | Earliest allowed checkout time. | No; model default is used if omitted. |
| `Settings:LatestCheckOutTime` | Latest allowed checkout time. | No; model default is used if omitted. |
| `Settings:TurnaroundMinutes` | Room turnaround interval. | No; model default is used if omitted. |
| `Settings:MaxStayNights` | Maximum stay length. | **Yes**; must be 1 through 365. |
| `Settings:GraceHours` | Free late-checkout grace period. | **Yes**; must be 0 through 24. |
| `Settings:CheckOutReminderLeadMinutes` | Reminder lead time. | **Yes**; must be 5 through 2880. |
| `Settings:ReceptionNotificationEmails` | Staff inboxes for checkout reminders. | No; empty uses the admin notification address. |
| `Jobs:PendingExpiryIntervalMinutes` | Pending-booking cleanup interval. | **Yes**; must be greater than zero. |
| `Jobs:VerificationCleanupIntervalMinutes` | Pending-verification cleanup interval. | **Yes**; must be greater than zero. |
| `Jobs:CheckOutReminderIntervalMinutes` | Checkout-reminder worker interval. | **Yes**; must be greater than zero. |
| `BookingRetention:Enabled` | Enables cancelled-booking retention cleanup. | No; default is true. |
| `BookingRetention:CancelledDays` | Cancelled-booking retention period. | **Yes**; must not be negative. |
| `BookingRetention:IntervalMinutes` | Retention cleanup interval. | **Yes**; must be greater than zero. |
| `Auth:CodeMinutes` | OTP and password-reset code lifetime. | **Yes**; must be greater than zero. |
| `Auth:ResendCooldownSeconds` | OTP resend cooldown. | **Yes**; must not be negative. |
| `Auth:MaxSendsPerHour` | OTP send limit per hour. | **Yes**; must be greater than zero. |
| `Auth:PendingRegistrationHours` | Pending-registration retention period. | **Yes**; must be greater than zero. |
| `Auth:MaxFailedAttempts` | Failed OTP attempts before invalidation. | **Yes**; must be greater than zero. |
| `Security:OtpHmacKey` | HMAC key for OTP hashing. | **Yes**; must be at least 32 UTF-8 bytes. |
| `Cors:AllowedOrigins` | Exact browser origins allowed by the `Frontend` CORS policy. | **Yes**; must contain at least one absolute URL. |
| `RateLimiting:Auth:*` | Authentication request limit and window. | **Yes**; both values must be positive. |
| `RateLimiting:Otp:*` | OTP request limit and window. | **Yes**; both values must be positive. |
| `RateLimiting:Lookup:*` | Lookup request limit and window. | **Yes**; both values must be positive. |
| `RateLimiting:BookingCreate:*` | Booking-create request limit and window. | **Yes**; both values must be positive. |
| `RateLimiting:Contact:*` | Contact request limit and window. | **Yes**; both values must be positive. |
| `Email:Smtp:Host` | SMTP server hostname. | No; SMTP is optional because the logging fallback is used without credentials. |
| `Email:Smtp:Port` | SMTP server port. | **Yes**; must be a valid TCP port. |
| `Email:Smtp:Username` | SMTP username. | No for startup; required for real email delivery. |
| `Email:Smtp:Password` | SMTP password. | No for startup; required for real email delivery. |
| `Email:Smtp:FromAddress` | SMTP sender address. | No for startup; required with credentials for real email delivery. |
| `Email:Smtp:FromName` | Display name for outgoing email. | No. |
| `Email:AdminNotificationAddress` | Staff notification mailbox. | No; leave empty to disable staff alerts. If set, it must be a deliverable-looking address. |
| `Email:FrontendBaseUrl` | Frontend URL inserted into email links. | **Yes**; must be an absolute URL. |
| `Email:RequireConfirmedEmail` | Legacy/example email-confirmation setting. | No; it is not part of the current `EmailOptions` model. |
| `Cloudinary:CloudName` | Cloudinary account name for image uploads. | No for startup; required for image uploads. |
| `Cloudinary:ApiKey` | Cloudinary API key. | No for startup; required for image uploads. |
| `Cloudinary:ApiSecret` | Cloudinary API secret. | No for startup; required for image uploads. |

Do not print or commit real SMTP, JWT, OTP, or Cloudinary secrets. For a personal machine, use the ignored Development file or .NET user secrets.

### 5. Run the API

Use the HTTP launch profile explicitly:

```powershell
dotnet run --project .\src --launch-profile http
```

The `http` profile sets `ASPNETCORE_ENVIRONMENT=Development` and listens on `http://localhost:5144`. The `https` profile listens on `https://localhost:7041` and also configures `http://localhost:5144`.

On the first Development start, the API:

1. Applies pending EF Core migrations.
2. Creates the `Administrator`, `Receptionist`, and `Customer` roles if missing.
3. Seeds the administrator when both admin seed credentials are configured.
4. Seeds the configured demo receptionist and customer.
5. Seeds the three room types, surcharge tiers, and 20 sample rooms without replacing existing rows.
6. Seeds four sample bookings for the demo customer when that customer has no bookings.

The migration and seed calls are Development-only. Production does not create the schema during application startup.

Development API documentation is available from the Scalar/OpenAPI mappings once the API is running. The exact browser URL is not configured in this repository; use the API base URL and the generated development documentation route shown in the startup output.

## Demo accounts

The example configuration creates these development-only accounts:

| Role | Email | Password location |
|---|---|---|
| Administrator | `admin@example.com` | `Seed:AdminPassword` in `src\appsettings.Development.json` |
| Receptionist | `receptionist@deutchezz.local` | `Seed:ReceptionistPassword` in `src\appsettings.Development.json` |
| Customer | `customer@deutchezz.local` | `Seed:CustomerPassword` in `src\appsettings.Development.json` |

These are development-only demo credentials. Change them before sharing a development environment and never use them in production.

## SMTP and Cloudinary

### SMTP

SMTP placeholders do not prevent the API from starting. When SMTP username/password and a sender address are not configured, the application selects `LoggingEmailSender`, which logs that an email was not sent instead of failing the business operation.

With placeholders, OTP/verification emails, password-reset emails, booking notifications, and other transactional emails are not delivered. Email-dependent flows that require the recipient to receive a code, especially registration verification and password reset, cannot be completed through email until SMTP is configured.

To enable SMTP, edit these keys in the ignored Development file with values from your mail provider:

```text
Email:Smtp:Host
Email:Smtp:Port
Email:Smtp:Username
Email:Smtp:Password
Email:Smtp:FromAddress
Email:Smtp:FromName
```

`Email:AdminNotificationAddress` and `Settings:ReceptionNotificationEmails` may be left empty when staff notifications are not needed.

### Cloudinary

Cloudinary placeholders do not prevent startup. Image uploads still use `CloudinaryImageUploadService`; with invalid placeholder credentials, image upload requests return an upload failure/error. Configure `Cloudinary:CloudName`, `Cloudinary:ApiKey`, and `Cloudinary:ApiSecret` with a real Cloudinary account to enable room-type image uploads.

## Ports and CORS

The configured API URLs are:

- HTTP: `http://localhost:5144`
- HTTPS: `https://localhost:7041`
- HTTPS profile also binds HTTP on `http://localhost:5144`
- PostgreSQL: `localhost:5432`

The default allowed frontend origins are:

- `http://localhost:5173`
- `http://localhost:3000`

To add another frontend origin, add its exact absolute origin to `Cors:AllowedOrigins` in `src\appsettings.Development.json`, for example:

```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:5173",
    "http://localhost:3000",
    "http://localhost:4173"
  ]
}
```

Do not include a path or trailing API route in an origin.

## Connecting the frontend

The backend controllers use the `/api/v1` path prefix. Set the frontend environment variable to the HTTP API base URL:

```text
VITE_API_URL=http://localhost:5144/api/v1
```

For the HTTPS launch profile, use:

```text
VITE_API_URL=https://localhost:7041/api/v1
```

## Troubleshooting

### PostgreSQL port 5432 is already in use

Find the process using the port:

```powershell
Get-NetTCPConnection -LocalPort 5432 -State Listen
```

Stop only the specific process after confirming it is safe, or stop the other PostgreSQL/container service. Alternatively, change the host-side compose port and update `ConnectionStrings:DefaultConnection` to the new host port. Keep the container port `5432`.

### Migration or database permission errors

Confirm that PostgreSQL is healthy and that the connection string points to the same host, port, database, username, and password:

```powershell
docker compose ps
docker compose logs db
```

For a local PostgreSQL installation, verify that the configured user owns `deutchezz` or has permission to create and alter tables. The API applies migrations automatically only in non-Production environments.

### JWT or OTP key too short

`Jwt:Key` and `Security:OtpHmacKey` must each be at least 32 UTF-8 bytes. Use the safe values from the example for local development, or generate separate keys in PowerShell:

```powershell
$bytes = New-Object byte[] 64
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

Put the generated values in the ignored Development file or store them with .NET user secrets.

### CORS error

The browser origin must exactly match one of `Cors:AllowedOrigins`, including scheme and port. Add the frontend origin to the Development configuration and restart the API. `localhost` and `127.0.0.1` are different origins.

### Reset the development database

This removes the PostgreSQL container volume and all local development data:

```powershell
docker compose down -v
docker compose up -d
```

Start the API again to apply migrations and recreate the Development seed data.

## Running tests

Run the complete solution test suite from the repository root:

```powershell
dotnet test
```

Build without running tests:

```powershell
dotnet build .\Deutchezz.sln
```

The repository contains architecture, unit, and integration test projects under `tests\`. Integration tests use their configured test database settings; make sure the required PostgreSQL/test database is available before running the full suite.
