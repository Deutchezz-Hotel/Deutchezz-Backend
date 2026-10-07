# Backend Audit — Deutchezz Hotel

> Audited: 2026-10-07 · Solution: `Deutchezz.sln` · Framework: .NET 9 / ASP.NET Core / EF Core (PostgreSQL) / Identity + JWT

---

## 1. Single project or 4 projects?

**4 projects in 1 folder, one solution — not 4 separate apps.** It is a single deployable backend (one Clean-Architecture monolith) split into layered assemblies:

- `Deutchezz.sln` → 5 projects: **Domain, Application, Infrastructure, Api** (in `src/`) + **Deutchezz.ArchitectureTests** (in `tests/`)
- Dependency direction: `Api → Application → Domain`, `Infrastructure → Application + Domain` (enforced by NetArchTest — 4/4 tests pass)
- History note: the repo previously held **1 monolithic project** `Deutchezz.Backend/` (its own `src/Presentation` + `src/Domain` + migrations + `PROJECT_STRUCTURE.md`). It has been **deleted from the working tree but is still tracked in git (uncommitted deletions)** and still referenced by the `.sln`.

---

## 2. Project structure

```markdown
Deutchezz_Hotel/
├── Deutchezz.sln                     # ⚠ still references deleted Deutchezz.Backend.csproj
├── src/
│   ├── Domain/                       # Zero dependencies (entities, enums, Result)
│   │   ├── Common/
│   │   │   ├── BaseEntity.cs         # BaseEntity → AuditableEntity → SoftDeletableEntity
│   │   │   ├── Result.cs             # Result / Result<T> (no exceptions for flow control)
│   │   │   └── Error.cs              # Error record + ErrorType enum
│   │   ├── Entities/                 # 10 entities: User, Role, Guest, Room, RoomType,
│   │   │                             # Booking, BookingRoomType, RoomAssignment,
│   │   │                             # Payment, Notification, ActivityLog
│   │   ├── Enums/                    # BookingStatus, PaymentStatus/Method,
│   │   │                             # RoomStatus, NotificationType, UserRole
│   │   └── Class1.cs                 # 🗑 placeholder junk
│   │
│   ├── Application/                  # Use cases + contracts (no EF/HTTP)
│   │   ├── Abstractions/             # IClock, IEmailSender, ITokenService,
│   │   │                             # IActivityLogger, IBookingReferenceGenerator,
│   │   │                             # IDbConflictDetector
│   │   ├── Data/IAppDbContext.cs     # DbSet surface + SaveChangesAsync (port)
│   │   ├── Common/                   # PaginatedResult<T>, QueryParameters (page ≤100)
│   │   ├── Auth/                     # IAuthService + AuthService + Dtos
│   │   ├── Availability/             # IAvailabilityService + Service + Dtos
│   │   ├── Bookings/                 # IBookingService + Service (402 lines) + Dtos
│   │   ├── Rooms/  RoomTypes/        # I*Service + Service + Dtos each
│   │   ├── Payments/ Profile/        # I*Service + Service + Dtos each
│   │   ├── Notifications/            # I*Service + Service + Dtos
│   │   ├── ActivityLogs/             # 🗑 empty (no query/reporting side)
│   │   ├── Reports/                  # 🗑 empty (not implemented)
│   │   ├── DependencyInjection.cs    # AddApplication() — 8 scoped services
│   │   └── Class1.cs                 # 🗑 placeholder junk
│   │
│   ├── Infrastructure/               # EF Core (PostgreSQL) + JWT + adapters
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs       # IdentityDbContext<User,Role,Guid>, IAppDbContext,
│   │   │   │                         # global soft-delete query filter, UpdatedAt stamping
│   │   │   ├── Configurations/       # 10 IEntityTypeConfiguration (one per entity)
│   │   │   └── Migrations/           # 🗑 EMPTY — no migrations exist
│   │   ├── Services/                 # TokenService (JWT+refresh rotation), EmailSender,
│   │   │                             # ActivityLogger, BookingReferenceGenerator,
│   │   │                             # SystemClock, NpgsqlConflictDetector (40001/40P01)
│   │   ├── Seeders/Seeder.cs         # roles (Admin/Receptionist/Guest), admin user,
│   │   │                             # 3 room types + 20 rooms (dev only)
│   │   ├── DependencyInjection.cs    # AddInfrastructure() — DbContext, Identity, JWT
│   │   ├── Extensions/               # 🗑 empty
│   │   └── Class1.cs                 # 🗑 placeholder junk
│   │
│   └── Api/                          # ASP.NET Core host (net9.0, only entry point)
│       ├── Program.cs                # versioning, JSON string enums, OpenAPI, seed on dev
│       ├── Controllers/
│       │   ├── AuthController.cs     # register/login/refresh/logout/me
│       │   ├── RoomTypesController.cs# GET list + GET by id only
│       │   ├── Admin/                # 🗑 EMPTY (no admin endpoints)
│       │   ├── Me/                   # 🗑 EMPTY (no guest self-service endpoints)
│       │   └── Reception/            # 🗑 EMPTY (no check-in/out endpoints)
│       ├── Extensions/ResultExtensions.cs  # Result → IActionResult, GetUserId/GetRoles
│       ├── appsettings*.json         # ⚠ no ConnectionStrings, no Jwt, no Seed sections
│       └── Properties/launchSettings.json
│
└── tests/
    └── Deutchezz.ArchitectureTests/  # 4 NetArchTest rules (layer deps) — PASSING
```

---

## 3. Pattern walkthrough — 2 entities (domain → API)

### A. `RoomType` — the standard CRUD slice (fully wired)

| Layer | File | Role |
|---|---|---|
| Domain | `Domain/Entities/RoomType.cs` | `SoftDeletableEntity` + `Name/NightlyRate/MaxOccupancy/IsActive` |
| Infra | `Infrastructure/Data/Configurations/RoomTypeConfiguration.cs` | table `RoomTypes`, `decimal(18,2)` |
| App | `Application/RoomTypes/{IRoomTypesService,Dtos,RoomTypesService}.cs` | `RoomTypeQuery : QueryParameters` in, `RoomTypeResponse` record out; every method returns `Result<T>`; validates, checks duplicates → `Error.Conflict`, writes `IActivityLogger` audit line, deactivation blocked if future bookings exist |
| Api | `Api/Controllers/RoomTypesController.cs` | `[Route("api/v1/[controller]")]`, ctor-injected service, `result.ToActionResult()` maps `ErrorType` → HTTP status |

**Pattern:** *entity (Domain) → EF configuration (Infra) → interface + DTO records + service returning `Result<T>` (App) → thin controller + `ResultExtensions` mapping (Api).*

### B. `Booking` — the complex write slice (service complete, **no controller yet**)

- **Domain:** `Booking` (`AuditableEntity`, `BookingStatus`, `PaymentStatus`, `byte[]? ConcurrencyToken`) + `BookingRoomType` snapshot join (nightly rate frozen at booking time).
- **Application:** `BookingService.cs` (402 lines) — create in transaction with retry (`MaxAttempts = 3`, `IDbConflictDetector` on Postgres 40001/40P01), guest find-or-create, lookup/cancel by reference, search + pagination, assign-and-check-in, check-out, no-show; all via `IAppDbContext`, all returning `Result<T>`, all audited through `IActivityLogger`.
- **Api:** **missing.** `Controllers/Reception/`, `Me/`, `Admin/` are empty folders — 6 of 8 application services (Bookings, Rooms, Payments, Profile, Notifications, Availability) have **zero HTTP endpoints**.

---

## 4. Findings

### Blockers

1. **`dotnet build Deutchezz.sln` fails** — the `.sln` still points at the deleted `Deutchezz.Backend/Deutchezz.Backend.csproj` (`MSB3202`). Building `src/Api/Api.csproj` and the test project individually succeeds: 0 warnings, 0 errors; 4/4 architecture tests pass.
2. **No EF migrations and no `Migrate()`/`EnsureCreated()` anywhere** — `Infrastructure/Data/Migrations/` is empty and nothing creates the schema; against a fresh PostgreSQL database the app will crash on first query/seeding.
3. **`appsettings.json` has no `ConnectionStrings:DefaultConnection`, no `Jwt:*`, no `Seed:*` sections** — `AddInfrastructure` and `TokenService` will throw `Jwt:Key not configured` at runtime.

### Coverage gaps

4. Only 2 of ~10 planned controllers exist (the old monolith had Admin/Me/Reception/Availability/Bookings/Rooms/Dashboard controllers — deleted, not yet ported).
5. Empty modules: `Application/ActivityLogs`, `Application/Reports`, `Infrastructure/Extensions`, `Application/Abstractions/Services`.
6. No role-based policies (`AddAuthorization()` registered with no policies) — there are no `[Authorize(Roles = ...)]` rules to enforce admin/receptionist access yet.
7. `Class1.cs` placeholder files in Domain, Application, Infrastructure.

### Repo hygiene

8. **No `.gitignore`** — `bin/`, `obj/` are committed and currently showing as modified.
9. The old `Deutchezz.Backend/` deletions are uncommitted; docs (`PROJECT_STRUCTURE.md`, source-of-truth PDF, spec markdown) are staged for deletion but not committed.

### What's solid

- Clean layering with dependency rules enforced by passing architecture tests (4/4).
- Consistent `Result`/`Error` discipline — no exception-driven flow control in services.
- Identity + JWT with refresh-token rotation and hashing, global soft-delete query filter, automatic `UpdatedAt` stamping, concurrency-token handling, activity logging, seeder with roles/admin/sample inventory.

---

## 5. Suggested fix order

1. Clean `Deutchezz.sln` (remove stale `Deutchezz.Backend` entry), add `.gitignore`, commit the old-project deletion.
2. Add `ConnectionStrings`, `Jwt:*`, and `Seed:*` configuration to `appsettings.json` / `appsettings.Development.json`.
3. Create the initial EF migration and call `Database.Migrate()` at startup (before seeding).
4. Port the remaining controllers — Bookings, Availability, and Rooms first.
5. Delete `Class1.cs` placeholders and the empty placeholder folders, or fill them.
