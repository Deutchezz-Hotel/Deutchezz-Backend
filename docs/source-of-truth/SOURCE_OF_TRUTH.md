# DEUTCHEZZ HOTEL ISPAHAN — BACKEND SOURCE OF TRUTH

**Domain model, business rules & API contract**
**Version 1.5** · Location: Ahmadu Bello Way, Jos, Plateau State, Nigeria

Canonical backend design reference, to be used before and during implementation.

| Item | Agreed baseline |
| --- | --- |
| Backend | ASP.NET Core / C# |
| Database | PostgreSQL |
| ORM | Entity Framework Core |
| Authentication | ASP.NET Core Identity / secure JWT-based API authentication |
| Frontend consumer | React + TypeScript + Vite + Tailwind + shadcn/ui |
| Current payment model | Pay at hotel / receptionist-recorded payments |
| Online gateway | Deferred until provider account and requirements are available |
| Inventory | Dynamic; initial 20 rooms are sample data, not a maximum |

**Governance rule:** implementation follows this document. If a business or architectural requirement
changes, update this document first and version the change before altering the affected implementation.
Where a tracker or task description disagrees with this document, this document wins.

> **Archive note:** the v1.0 baseline is preserved unchanged at `Deut_Source of Truth.txt` (repo root).

---

## CHANGELOG

### v1.1 — 2026-10-10 (owner decisions applied)

Resolved by owner decision:
- **Pay-at-hotel is the active payment path.** New bookings are created with
  `BookingStatus.Confirmed` and `PaymentStatus.Pending`. `BookingStatus.Pending` is retained in the
  enum but reserved for the future online-payment flow (waiting on a gateway); nothing currently
  creates `Pending` bookings. The `Pending → Confirmed` transition remains valid.
- **Room prices are PER NIGHT.** `RoomType.BasePrice` is a nightly price in NGN
  (Standard 30000, Deluxe 40000, Executive 55000). The rate is snapshotted into
  `BookingRoomType.RatePerNight` at booking time. *(Resolves open decision "Meaning of n30k/n40k/n55k".)*
- **No taxes or fees.** `TotalAmount = SUM(BookingRoomType.Subtotal)`. Tax/service-charge percentage
  is not configured and not calculated. Sections 13 and 14 defer taxes until the hotel supplies a policy.
- **Wave order** is 0, 1, 3, 4, 2, 5 (Reception → Admin → Customer self-service → Reporting).
- **Wave 1 sub-pieces**, in order: 1a room types, 1b availability search, 1c booking create, 1d booking lookup.
- **Seeded room types confirmed** as Standard 30000, Deluxe 40000, Executive 55000 per night,
  plus the 20 sample rooms (8 Standard / 7 Deluxe / 5 Executive). Seeding only inserts missing
  rows and never updates existing ones.

Approved field additions (not in v1.0):
- `Booking.IdempotencyKey` (nullable, unique index) and `Booking.IdempotencyRequestHash` (nullable).
- `RoomType.ImageUrls` (list, max 8) and `RoomType.Features` (list, max 20) — display fields only,
  stored as `text[]`. Image file upload stays deferred; URLs only.
- `ActivityLog.UserId` is nullable so anonymous actions can be audited.

Explicitly **not** added (v1.0 stays authoritative): `Booking` has **no** `UserId`, `Subtotal` or
`TaxAmount` column. A registered user is linked through `Guest.UserId`; the confirmation `Notification`
is only created when that guest has a `UserId`.

New/confirmed rules recorded in v1.1:
- Booking create requires an `Idempotency-Key` header; same key + same payload returns the original
  booking, same key + different payload returns **409**.
- Insufficient availability returns **409**; multi-room/multi-type requests are all-or-nothing.
- Availability capacity = rooms with `IsActive = true` AND `Status` not `Maintenance` and not `Inactive`,
  minus quantities of overlapping bookings in `Pending`, `Confirmed` or `CheckedIn`.
- Concurrency strategy: one transaction with `SELECT … FOR UPDATE` row locks on the requested
  `RoomType` rows, taken in ascending `RoomType.Id` order.
- Rate limiting, CORS and Gmail SMTP (behind `IEmailSender`) are in scope.
- Structured logging means `ILogger` message templates; Serilog sinks are deferred.

Still open (not implemented): pending/unpaid booking expiry, cancellation window and refund rules,
check-in payment rule, check-out balance rule, no-show status, guest data retention, the full room
catalogue beyond the three seeded types.

### v1.2 — 2026-10-10 (Wave 6: contract conformance & test coverage)

Owner-approved conformance work. No new domain entities and no new routes; it pins down shapes the
v1.1 conventions already required.

- **Errors are RFC 7807 `ProblemDetails` on every failure path** (validation 400, unauthenticated 401,
  forbidden 403, not found 404, conflict 409, concurrency 409, rate-limited 429, unhandled 500). In
  addition to the standard members, `ProblemDetails` carries a machine-readable `code` extension
  (e.g. `booking.insufficient_availability`). Model-binding failures from `[ApiController]` keep the
  framework `ValidationProblemDetails` shape with an `errors` field map.
- **List endpoints accept optional `sort` (field name) and `direction` (`asc`|`desc`)**, in addition to
  `page`/`pageSize`. An unknown/blank `sort` falls back to the endpoint default; an unknown `direction`
  falls back to `asc`. The `PagedResult` envelope and its defaults are unchanged.
- **Concrete list/detail DTO families are fixed** (implementations of the §8 catalog): 
  `BookingListItemResponse` (list) vs `BookingDetailResponse` (detail),
  `RoomListItemResponse` vs `RoomResponse`, `RoomTypeListItemResponse` vs `RoomTypeResponse`,
  `PaymentListItemResponse` vs `PaymentResponse`, plus `CheckInResponse`, `CheckOutResponse`,
  `RoomAssignmentResponse` and `NotificationResponse`. List DTOs stay lightweight; detail DTOs are rich.
- **Integration test project is exercised** against a dedicated `deutchezz_test` database covering the
  critical paths: auth, availability search, booking create (idempotency replay + conflict), lookup,
  reception assign/check-in/check-out, admin inventory/staff/settings CRUD and dashboard/reports.

Conformance defects fixed while implementing v1.2 (all were latent 500s on previously untested paths):
- **List ordering is applied to a translatable projection.** Ordering by a member of a constructed DTO
  could not be translated by EF Core; affected room listing ordered by an anonymous join projection and
  mapped to the response DTO only after materialization.
- **All 401/403/429 responses are `application/problem+json`.** Writing via `WriteAsJsonAsync` after
  setting `ContentType` reset it to `application/json`; the content type is now passed to the writer.
- **Date range filters use UTC bounds.** Bounds were built with `DateTime` `Kind=Unspecified`, which
  Npgsql rejects for `timestamp with time zone` columns; day boundaries are now UTC (`UtcStartOfDay`).

### v1.3 — 2026-10-11 (payment lists for reception + richer list rows)

- **Reception can list payments.** New endpoint `GET /api/v1/reception/payments`
  (Receptionist/Admin, same `PaymentQuery` filters and `sort`/`direction`/paging as the admin list).
  Previously only `POST /api/v1/reception/payments` existed.
- **`PaymentListItemResponse` enriched** for payment tables: it now also returns
  `BookingReference`, `GuestName` and `Reference` (the payment transaction reference) in addition to
  `Id, BookingId, Amount, Method, Status, CreatedAt`. This makes the admin and reception payment tables
  show booking reference, client name and transaction reference without a per-row detail lookup.

### v1.4 — 2026-10-11 (date-based room board, anonymous cancellation, transactional email)

- **Date-based room availability board.** The room list endpoints
  (`GET /api/v1/admin/rooms`, `GET /api/v1/reception/rooms`) accept `date` (`yyyy-MM-dd`, default
  today) and `search` (matches room number or room type name). For the requested date a room's
  **effective status** is: `Maintenance`/`Inactive` when the physical status says so; otherwise
  `Occupied` when an active `RoomAssignment` to a `Confirmed`/`CheckedIn` booking covers the date
  (`CheckInDate ≤ date < CheckOutDate`); otherwise `Available`. Both `RoomResponse` and
  `RoomListItemResponse` now return `effectiveStatus` alongside the physical `status`. The status
  filter now matches the effective status for the requested date. This makes the board reflect real
  occupancy per date instead of the last check-in/check-out mutation.
- **Physical room double-assignment is rejected.** `AssignRooms` now returns **409**
  (`booking.room_unavailable`) if a requested room is already held by another live
  (`Confirmed`/`CheckedIn`) booking whose dates overlap (BR-023). Previously overlapping assignments
  were silently allowed.
- **Anonymous booking cancellation.** New public endpoint `POST /api/v1/bookings/cancel`
  (`AnonymousCancelBookingRequest { bookingReference, email, reason? }`, rate limit `lookup`),
  mirroring lookup ownership. Registered customers continue to use
  `POST /api/v1/me/bookings/{id}/cancel`.
- **Transactional emails are now sent.** Booking confirmation, cancellation confirmation, payment
  receipt and check-in/check-out notices are HTML emails built in the Application layer
  (`Deutchezz.Application.Emails`) and delivered through `IEmailSender`. `SmtpEmailSender` never
  throws — a delivery failure is logged and never fails the business operation. With no SMTP
  credentials configured the logging sender is used.

### v1.5 — 2026-10-11 (operational policies: expiry, cancellation window & refund, payment gates)

Resolved by owner decision (§15). These policies are configurable and **default to the least
restrictive behaviour**, so out-of-the-box flows (pay-at-hotel, check-in without prepayment, receipts
recorded after the stay) are unchanged:

- **Pending booking expiry (7e).** A `Pending` booking is auto-cancelled when it has been `Pending`
  for longer than `Cancellation:WindowHours`. A background worker runs every
  `Jobs.PendingExpiryIntervalMinutes` (default 15) minutes, marks the booking `Cancelled` with reason
  "Expired reservation", writes an ActivityLog (`booking.expired`, actor null) and posts a
  `BookingCancelled` notification only when the guest has an account. No email is sent. Under the
  current pay-at-hotel model nothing creates `Pending` bookings yet (created `Confirmed`), so the job
  is dormant-but-correct and activates with the future online-payment flow.
- **Cancellation window (7d).** A booking can be cancelled without charge only until
  `Cancellation:WindowHours` hours **before the check-in day starts** in Africa/Lagos. When the
  deadline has passed, guest and anonymous cancels are rejected with **409**
  `booking.cancellation_window_closed`. Reception/administrators have no cancel endpoint, so the rule
  is uniform.
- **Refund rule (7d).** On any permitted cancellation, all `Paid` `Payment` rows for the booking are
  marked `Refunded`, `Booking.PaymentStatus` becomes `Refunded`, and the cancellation email states the
  refunded amount. Because late cancellations are blocked, no partial fee is computed.
- **Payment gates (7f).** New settings `Settings:RequireFullPaymentAtCheckIn` and
  `Settings:RequirePaymentBeforeCheckOut` (both default `false`). When a gate is on, check-in/check-out
  with an outstanding balance returns **409** `booking.balance_outstanding`. Both are editable through
  the admin settings API/page; no DB migration is needed (key/value `Setting` rows).

### v1.0 — baseline
Original document (archived as `Deut_Source of Truth.txt`).

---

## 1. SYSTEM SCOPE

- Public hotel website and room discovery.
- Date-based room availability search.
- Anonymous and registered-user bookings.
- Multi-room bookings in one reservation.
- Customer booking dashboard and anonymous booking lookup.
- Receptionist reservation, room assignment, check-in, check-out, guest and payment operations.
- Administrator inventory, room type, staff, guest, payment, reporting, notification, settings and audit operations.
- Backend-controlled pricing, totals, authorization, state transitions and availability.

## 2. ARCHITECTURAL PRINCIPLES

- The backend is authoritative; frontend totals and status values are never trusted.
- Anonymous and registered bookings use the same Booking domain.
- A reservation describes requested room types and quantities; physical room assignment is a separate
  operational concern.
- Historical reservation rates are stored so later price changes do not alter existing bookings.
- Booking status and payment status are separate.
- Inventory-changing operations require appropriate transactions and concurrency protection.
- Important operational and administrative actions are auditable.

`React Frontend → ASP.NET Core API → Application/Domain → Infrastructure/EF Core → PostgreSQL`

## 3. BASE CLASSES

| Base class | Members | Use |
| --- | --- | --- |
| BaseEntity | Id | Common primary key. |
| AuditableEntity | CreatedAt, UpdatedAt | Creation/modification timestamps. |
| SoftDeletableEntity (selective) | IsDeleted, DeletedAt | Only for entities where non-destructive deletion is required. |

Bookings and payments are retained for history/audit rather than physically deleted.

## 4. ENUMS

| Enum | Values | Purpose |
| --- | --- | --- |
| UserRole | Customer, Receptionist, Administrator | Authorization. |
| RoomStatus | Available, Occupied, Maintenance, Inactive | Physical room operational state. |
| BookingStatus | Pending, Confirmed, CheckedIn, CheckedOut, Cancelled | Reservation lifecycle. |
| PaymentStatus | Pending, PartiallyPaid, Paid, Refunded, Failed | Payment state. |
| PaymentMethod | Cash, POS, BankTransfer, Online | Payment channel. |
| NotificationType | BookingConfirmed, PaymentReceived, BookingCancelled, CheckInCompleted, CheckOutCompleted | Notification classification. |

`BookingStatus.Pending` is reserved for the future online-payment flow; pay-at-hotel bookings are
created `Confirmed`. No `NoShow` status exists — do not add one without a versioned change here.

## 5. DOMAIN ENTITIES

**User** — Id, FirstName, LastName, Email, PhoneNumber, identity-managed credentials, Role, IsActive,
CreatedAt, UpdatedAt, LastLoginAt. *Authenticated application account.*

**Guest** — Id, UserId (nullable), FirstName, LastName, Email, PhoneNumber, CreatedAt, UpdatedAt.
*Hotel guest. `UserId` is nullable for anonymous guests and is the only link from a booking to a
registered user.*

**RoomType** — Id, Name, Description, BasePrice, MaximumOccupancy, BedType, ImageUrls (≤ 8 URLs),
Features (≤ 20 entries), IsActive, CreatedAt, UpdatedAt.
*Configurable room category and current base pricing (per night).*

**Room** — Id, RoomNumber, RoomTypeId, Floor, Status, IsActive, CreatedAt, UpdatedAt.
*Physical hotel room; administrators can add rooms dynamically.*

**Booking** — Id, BookingReference, GuestId, CheckInDate, CheckOutDate, Adults, Children, Status,
PaymentStatus, SpecialRequests, TotalAmount, IdempotencyKey (nullable, unique),
IdempotencyRequestHash (nullable), CreatedAt, UpdatedAt.
*Reservation aggregate root. No `UserId`, `Subtotal` or `TaxAmount` columns.*

**BookingRoomType** — Id, BookingId, RoomTypeId, Quantity, RatePerNight, NumberOfNights, Subtotal.
*Reservation line describing room type and quantity; preserves agreed rate.*

**RoomAssignment** — Id, BookingId, BookingRoomTypeId, RoomId, AssignedAt, AssignedByUserId, ReleasedAt (nullable).
*Maps reserved quantities to actual physical rooms; `ReleasedAt IS NULL` means the assignment is still live.*

**Payment** — Id, BookingId, Amount, PaymentMethod, Status, Reference, PaidAt, RecordedByUserId, Notes, CreatedAt.
*One payment event; multiple payments may belong to one booking.*

**Notification** — Id, UserId (nullable), GuestId (nullable), Type, Title, Message, IsRead, CreatedAt.
*In-app notification for authenticated users; created only when the booking's guest has a UserId.*

**ActivityLog** — Id, UserId (nullable), Action, EntityType, EntityId, Description, CreatedAt.
*Audit trail for important actions; anonymous actions store a null UserId and the booking reference
in Description.*

## 6. ENTITY RELATIONSHIPS

- Guest 1 → ∗ Booking
- Booking 1 → ∗ BookingRoomType
- RoomType 1 → ∗ BookingRoomType
- Booking 1 → ∗ RoomAssignment
- BookingRoomType 1 → ∗ RoomAssignment
- Room 1 → ∗ RoomAssignment (across different reservations/dates)
- Booking 1 → ∗ Payment
- User 1 → ∗ Notification
- User 1 → ∗ ActivityLog

Core distinction: the customer can reserve 2 Deluxe + 1 Executive without selecting physical room
numbers. Reception later assigns actual rooms such as Deluxe → 101, Deluxe → 102, Executive → 201.

## 7. CORE BUSINESS RULES

- **BR-001** Anonymous booking is allowed without account creation.
- **BR-002** Registered customers use the same booking domain and can view their reservations in their dashboard.
- **BR-003** Registration may be encouraged but must not be mandatory for guest checkout.
- **BR-004** Anonymous booking lookup requires booking reference plus guest email.
- **BR-005** A booking can contain multiple room types and quantities; there is no one-room-per-booking assumption.
- **BR-006** Customers reserve room types/quantities; physical rooms are assigned separately.
- **BR-007** A room is unavailable for a stay when a conflicting active assignment/reservation occupies the requested date range.
- **BR-008** Check-in date is inclusive and check-out date is exclusive for night and overlap calculations.
- **BR-009** Check-out must be after check-in; nights are calculated server-side.
- **BR-010** Backend calculates prices and totals. Client-submitted totals are never authoritative.
- **BR-011** BookingRoomType stores the agreed rate snapshot.
- **BR-012** Booking status and payment status are separate.
- **BR-013** A booking may have multiple payment records; paid amount is calculated from valid payment records.
- **BR-014** Current MVP supports manual Cash, POS and Bank Transfer recording; online gateway integration is deferred.
- **BR-015** Check-in changes assigned rooms to Occupied; check-out releases them to Available unless another operational status applies.
- **BR-016** Check-in validates booking state, dates, assignments and configured payment policy.
- **BR-017** Check-out validates that the booking is currently checked in and follows the configured outstanding-balance policy.
- **BR-018** Cancelled bookings do not consume room availability.
- **BR-019** Customer, Receptionist and Administrator permissions are enforced by the backend.
- **BR-020** Important administrative and operational actions generate audit records.
- **BR-021** The initial 20 rooms are sample inventory only; administrators can add more.
- **BR-022** Booking/inventory changes use transactions and concurrency controls sufficient to prevent overbooking.
- **BR-023** A physical room cannot be assigned to two overlapping live (`Confirmed`/`CheckedIn`)
  bookings; a conflicting assignment request is rejected with **409**.

### 7a. Pricing

- `RoomType.BasePrice` is **per night** (NGN).
- `BookingRoomType.RatePerNight` = `BasePrice` at booking time (snapshot; later price changes do not affect it).
- `BookingRoomType.Subtotal` = `RatePerNight × NumberOfNights × Quantity`.
- `Booking.TotalAmount` = Σ `BookingRoomType.Subtotal`. **No tax, no service charge, no fees.**
- Money is `decimal(18,2)`, rounded to 2 decimals with `MidpointRounding.AwayFromZero`.
- Client-submitted prices/totals are ignored.

### 7b. Availability

- Overlap is half-open: an existing booking overlaps when
  `existing.CheckInDate < requested.CheckOutDate AND existing.CheckOutDate > requested.CheckInDate`.
  A guest checking out on the 10th does **not** block a guest checking in on the 10th (back-to-back stays allowed).
- **Capacity** of a room type = count of rooms where `IsActive = true` AND `Status` is neither
  `Maintenance` nor `Inactive` AND not soft-deleted. (`Occupied` is a current operational status and does
  not reduce capacity for future dates.)
- **Booked** = Σ of requested room quantities across overlapping bookings whose status is
  `Pending`, `Confirmed` or `CheckedIn`. `Cancelled` and `CheckedOut` bookings do not count.
- **Available** = capacity − booked, never below zero.
- Unknown or inactive room types are excluded from results.

### 7c. Booking create

- Requires header `Idempotency-Key` (client-generated GUID); missing header → 400.
- Same key + same payload (SHA-256 of the canonical payload) → return the original booking, HTTP 200, no
  second email. Same key + different payload → **409**.
- All-or-nothing: if any requested type lacks quantity the whole request is **409** naming the shortage.
- **Concurrency strategy:** one database transaction, `SELECT … FOR UPDATE` on the requested `RoomType`
  rows in ascending `Id` order (deadlock avoidance), then compute availability as in 7b, then insert.
  *(Chosen over Serializable-isolation-with-retry because it is deterministic and easier to test.)*
- State: `BookingStatus.Confirmed`, `PaymentStatus.Pending`.
- Booking reference format: `DHI-<yyyy>-<XXXXXX>` where `yyyy` is the creation year in **Africa/Lagos**
  and the suffix is 6 characters from the unambiguous alphabet (no `0`, `O`, `1`, `I`, `L`); unique
  index, regenerate on collision.
- Guest: authenticated customer → use (or create) the Guest whose `UserId` is that user. Anonymous →
  find or create a Guest by normalized (lowercased, trimmed) email **where `UserId IS NULL`**; never
  attach an anonymous booking to a registered user's Guest by email match.
- Specific physical rooms are **not** assigned at create (reception assigns later).
- After commit: ActivityLog entry (actor = user id or null with reference in Description), a
  `Notification` of type `BookingConfirmed` **only when the guest has a UserId**, and the confirmation
  email (failure never affects the response).
- Validation: check-in not before "today" in Africa/Lagos; check-out strictly after check-in; nights ≥ 1;
  adults ≥ 1; children ≥ 0; total rooms between 1 and `Settings:MaxRoomsPerBooking`; every room type
  exists and is active; guest email format; phone present; quantities positive.

### 7d. Booking cancellation (window & refund)

- Authority: registered owner via `POST /api/v1/me/bookings/{id}/cancel`; anonymous via reference +
  email on `POST /api/v1/bookings/cancel`. Both route to the same `CancelAsync`.
- **Window (BR-024):** cancellation is allowed, free of charge, until `Cancellation:WindowHours` hours
  before the check-in day start in Africa/Lagos (00:00 local on `CheckInDate`, minus the window). A
  cancellation attempted after that deadline fails with **409** `booking.cancellation_window_closed`.
  This applies to `Pending` and `Confirmed` bookings alike.
- **Refund (BR-025):** on a permitted cancellation every `Payment` row with `Status = Paid` is set to
  `Refunded`, the booking's `PaymentStatus` becomes `Refunded`, and the cancellation email names the
  refunded amount. Partial-penalty calculations are intentionally avoided because late cancellations
  are blocked.
- Recording: `CancelledAt = now (UTC)`, `CancellationReason = reason`, concurrency token bumped,
  ActivityLog (`booking.cancelled`) and a `BookingCancelled` notification (registered guests only).
  Blocks: already `Cancelled`/`CheckedIn`/`CheckedOut` → 409; registered owner must own the booking.

### 7e. Pending booking expiry (background job)

- Scope: `BookingStatus.Pending` only. Pay-at-hotel bookings are created `Confirmed` and are never
  touched by this job.
- Trigger: a `Pending` booking is expired once `CreatedAt ≤ now − Cancellation:WindowHours`.
- Worker: `PendingBookingExpiryWorker` (`BackgroundService`, Application layer), runs every
  `Jobs:PendingExpiryIntervalMinutes` minutes (default 15, validated `> 0`). Idle-safe: a single query
  finds nothing and the cycle sleeps.
- Expiry: status → `Cancelled`, `CancelledAt = now (UTC)`, `CancellationReason = "Expired reservation"`,
  concurrency token bumped, ActivityLog (`booking.expired`, actor null, entity Booking) and a
  `BookingCancelled` notification only when the guest has a UserId. No email is sent.

### 7f. Payment gates at check-in and check-out

- `Settings:RequireFullPaymentAtCheckIn` (default `false`): when `true`, check-in is rejected with
  **409** `booking.balance_outstanding` while `paid < TotalAmount`.
- `Settings:RequirePaymentBeforeCheckOut` (default `false`): when `true`, check-out is rejected with
  **409** `booking.balance_outstanding` while `paid < TotalAmount`.
- `paid` = Σ of `Payment` rows with `Status = Paid` for the booking. Both settings are DB-backed
  (`Setting` key/value rows, no migration) with appsettings fallback, and editable via
  `GET/PUT /api/v1/admin/settings` and the admin Settings page.

## 8. DTO / CONTRACT CATALOG

| Area | Request DTOs | Response DTOs |
| --- | --- | --- |
| Auth | RegisterRequest, LoginRequest | LoginResponse, CurrentUserResponse |
| Availability | AvailabilitySearchRequest | AvailabilityResponse, AvailableRoomTypeResponse |
| Booking | CreateBookingRequest, BookingLookupRequest, CancelBookingRequest, AnonymousCancelBookingRequest | BookingResponse, BookingSummaryResponse, BookingDetailResponse, BookingListItemResponse |
| Assignment | AssignRoomsRequest | RoomAssignmentResponse |
| Stay | CheckInRequest, CheckOutRequest | CheckInResponse, CheckOutResponse |
| Rooms | CreateRoomRequest, UpdateRoomRequest | RoomResponse, RoomListItemResponse |
| Room types | CreateRoomTypeRequest, UpdateRoomTypeRequest | RoomTypeResponse, RoomTypeListItemResponse |
| Payments | RecordPaymentRequest | PaymentResponse, PaymentListItemResponse |
| Profile | UpdateProfileRequest | ProfileResponse |
| Notifications | MarkNotificationReadRequest or command route | NotificationResponse |
| Reports | ReportFilterRequest | DashboardResponse, ReportResponse |

DTO rules:
- Do not expose EF Core entities directly from controllers.
- Separate write DTOs from read DTOs where their shapes differ.
- Validate input at the API boundary, then enforce business rules in application/domain logic.
- Use lightweight list DTOs for tables and richer detail DTOs for detail pages.
- Lookup request fields are `bookingReference` and `email`.

## 9. API CONTRACT CATALOG

| Area | Method | Route | Authorization |
| --- | --- | --- | --- |
| Auth | POST | /api/v1/auth/register | Public |
| Auth | POST | /api/v1/auth/login | Public |
| Auth | POST | /api/v1/auth/refresh | Public |
| Auth | POST | /api/v1/auth/logout | Authenticated |
| Auth | GET | /api/v1/auth/me | Authenticated |
| Rooms | GET | /api/v1/room-types | Public |
| Rooms | GET | /api/v1/room-types/{id} | Public |
| Availability | POST | /api/v1/availability/search | Public |
| Booking | POST | /api/v1/bookings | Public/Auth |
| Booking lookup | POST | /api/v1/bookings/lookup | Public |
| Booking cancel | POST | /api/v1/bookings/cancel | Public |
| Customer | GET | /api/v1/me/bookings | Customer |
| Customer | GET | /api/v1/me/bookings/{id} | Customer |
| Customer | POST | /api/v1/me/bookings/{id}/cancel | Customer |
| Customer | GET | /api/v1/me/profile | Customer |
| Customer | PUT | /api/v1/me/profile | Customer |
| Reception | GET | /api/v1/reception/bookings | Receptionist/Admin |
| Reception | GET | /api/v1/reception/bookings/{id} | Receptionist/Admin |
| Reception | POST | /api/v1/reception/bookings | Receptionist/Admin |
| Reception | POST | /api/v1/reception/bookings/{id}/assign-rooms | Receptionist/Admin |
| Reception | POST | /api/v1/reception/bookings/{id}/check-in | Receptionist/Admin |
| Reception | POST | /api/v1/reception/bookings/{id}/check-out | Receptionist/Admin |
| Reception | GET | /api/v1/reception/rooms | Receptionist/Admin |
| Reception | GET | /api/v1/reception/guests | Receptionist/Admin |
| Reception | GET | /api/v1/reception/payments | Receptionist/Admin |
| Reception | POST | /api/v1/reception/payments | Receptionist/Admin |
| Admin | GET | /api/v1/admin/dashboard | Administrator |
| Admin | GET, POST | /api/v1/admin/rooms | Administrator |
| Admin | GET, PUT | /api/v1/admin/rooms/{id} | Administrator |
| Admin | PATCH | /api/v1/admin/rooms/{id}/status | Administrator |
| Admin | GET, POST | /api/v1/admin/room-types | Administrator |
| Admin | GET, PUT | /api/v1/admin/room-types/{id} | Administrator |
| Admin | PATCH | /api/v1/admin/room-types/{id}/status | Administrator |
| Admin | GET | /api/v1/admin/bookings | Administrator |
| Admin | GET | /api/v1/admin/guests | Administrator |
| Admin | GET, POST | /api/v1/admin/staff | Administrator |
| Admin | GET, PUT | /api/v1/admin/staff/{id} | Administrator |
| Admin | PATCH | /api/v1/admin/staff/{id}/status | Administrator |
| Admin | GET | /api/v1/admin/payments | Administrator |
| Admin | GET | /api/v1/admin/reports | Administrator |
| Admin | GET | /api/v1/admin/activity-logs | Administrator |
| Admin | GET | /api/v1/admin/notifications | Administrator |
| Admin | GET, PUT | /api/v1/admin/settings | Administrator |

No endpoint outside this catalog may be added without updating this document first.

## 10. KEY CONTRACT EXAMPLES

```json
AvailabilitySearchRequest
{ "checkInDate": "2026-10-10", "checkOutDate": "2026-10-15", "adults": 4, "children": 0, "rooms": 3 }

CreateBookingRequest
{ "checkInDate": "2026-10-10", "checkOutDate": "2026-10-15", "adults": 4, "children": 0,
  "guest": { "firstName": "Grace", "lastName": "Ibrahim", "email": "grace@example.com",
             "phoneNumber": "+2340000000000" },
  "rooms": [ { "roomTypeId": "DELUXE-ID", "quantity": 2 }, { "roomTypeId": "EXECUTIVE-ID", "quantity": 1 } ],
  "specialRequests": null }

BookingLookupRequest
{ "bookingReference": "DHI-2026-ABC234", "email": "grace@example.com" }

AssignRoomsRequest
{ "assignments": [ { "bookingRoomTypeId": "LINE-1", "roomId": "ROOM-101" },
                   { "bookingRoomTypeId": "LINE-1", "roomId": "ROOM-102" },
                   { "bookingRoomTypeId": "LINE-2", "roomId": "ROOM-201" } ] }

RecordPaymentRequest
{ "bookingId": "BOOKING-ID", "amount": 100000, "method": "POS",
  "reference": "POS-REF-001", "notes": "Initial deposit" }
```

## 11. STATE TRANSITIONS

Booking: `Pending → Confirmed → CheckedIn → CheckedOut`; `Pending → Cancelled`; `Confirmed → Cancelled`;
`Pending → Cancelled` also happens via automatic expiry (7e) after the pending window elapses.
The backend must reject illegal ordinary transitions such as `CheckedOut → Pending`.
New bookings are created directly at `Confirmed` under the pay-at-hotel model.

Room: `Available ↔ Occupied`; `Available → Maintenance → Available`; operational state → `Inactive` when retired.
Room availability for future dates is reservation/assignment driven and is not the same as current `RoomStatus`.
The room list's `effectiveStatus` for a given `date` is derived from assignments (see v1.4), while
`status` remains the persisted physical state.

## 12. CONTRACT CONVENTIONS

- JSON: **camelCase** properties; enums serialized as **strings** using these exact names
  (`JsonStringEnumConverter`); dates as `"yyyy-MM-dd"`; money as decimal numbers.
- Errors: **RFC 7807 ProblemDetails** everywhere, with a machine-readable `code` extension
  (e.g. `booking.insufficient_availability`); model-binding failures use `ValidationProblemDetails`
  with an `errors` field map.
- HTTP mapping: validation → 400, unauthenticated → 401, forbidden → 403, not found → 404,
  conflict/insufficient availability/idempotency mismatch → 409, rate limited → 429.
- Lists: `PagedResult` envelope `{ items, page, pageSize, totalCount, totalPages }` with
  `page` (default 1), `pageSize` (default 20, max 100), optional `sort` (field name) and
  `direction` (`asc`|`desc`); unknown `sort` uses the endpoint default, unknown `direction` uses `asc`.
- All timestamps are stored in UTC; business dates use `Africa/Lagos`.
- Logging: `ILogger` with structured message templates (satisfies "structured logging").
- Secrets only in user secrets / environment; never committed.

## 13. NON-FUNCTIONAL REQUIREMENTS

- Versioned ASP.NET Core API under `/api/v1`.
- PostgreSQL + Entity Framework Core.
- Secure identity and password handling.
- Server-side role authorization (policies `AdminOnly`, `StaffOnly`, `CustomerOnly`).
- Global exception handling with consistent error responses.
- Structured logging and validation.
- Transactions for multi-step inventory-changing operations.
- Concurrency protection against overbooking.
- Audit logging for important administrative/operational actions.
- Pagination, filtering and sorting for operational list endpoints where appropriate.
- No secrets or payment credentials committed to source control.
- Rate limiting: `auth` (default 10/min per IP) on login/register/refresh, `lookup` (default 5/min)
  on booking lookup, `booking-create` (default 10/min) on booking create; 429 → ProblemDetails.
- CORS: named policy `Frontend`, origins from `Cors:AllowedOrigins` (default `http://localhost:5173`,
  `http://localhost:3000`), any header/method, no credentials (JWT in the Authorization header).
- Email: Gmail SMTP behind `IEmailSender` (SmtpEmailSender); falls back to a logging sender when
  credentials are absent; send failures never fail a booking. Booking confirmation, cancellation,
  payment receipt and check-in/check-out notices are delivered as branded HTML emails.

## 14. EXPLICITLY DEFERRED

- Paystack, Flutterwave or Stripe integration until the hotel provides an account/provider requirements.
- Payment gateway webhooks and automated reconciliation.
- Loyalty/reward points and membership tiers unless confirmed as actual requirements.
- Housekeeping module unless confirmed.
- Airport transfer and ancillary services unless confirmed.
- Dynamic/seasonal/weekend pricing, taxes, discounts and refunds until the hotel's policy is supplied.
- Serilog sinks (built-in `ILogger` only for now).
- Database-backed settings (the `ISettingsProvider` starts on appsettings).

## 15. OPEN BUSINESS DECISIONS

| Decision | Current recommendation |
| --- | --- |
| Exact room catalogue | Admin-configurable; prototype room types are not authoritative. |
| ~~Meaning of n30k/n40k/n55k~~ | **Resolved (v1.1):** per-night price of a room type. |
| Taxes/fees | Do not add until hotel policy is confirmed. |
| ~~Cancellation policy~~ | **Resolved (v1.5):** free cancellation until `WindowHours` before check-in (7d); paid amounts fully refunded on permitted cancels. |
| No-show | Add NoShow status if required by hotel operations (not added). |
| ~~Check-in payment rule~~ | **Resolved (v1.5):** configurable gate `RequireFullPaymentAtCheckIn`, default off (7f). |
| ~~Check-out outstanding balance~~ | **Resolved (v1.5):** configurable gate `RequirePaymentBeforeCheckOut`, default off (7f). |
| Room assignment | Assign before check-in is recommended. |
| ~~Pending booking expiry~~ | **Resolved (v1.5):** `Pending` expires after `Cancellation:WindowHours` via background job (7e). |
| Guest data retention | Define retention/privacy policy with the hotel. |

## 16. SOURCE-OF-TRUTH GOVERNANCE

- This document is the backend design baseline.
- Frontend prototype data is not automatically real hotel data.
- Business-rule changes should be recorded here before implementation changes.
- Material domain/API changes require a new document version.
- Implementation should not invent new domain entities merely because a UI prototype contains decorative features.

Implementation readiness: the core model, enum set, base-class direction, DTO families, API route
catalog and principal business rules are defined. The remaining hotel-policy decisions should be
confirmed before freezing this as the immutable production specification.
