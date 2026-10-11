# ADR-0001 — Wave 0 decisions

**Date:** 2026-10-10 · **Status:** Accepted · **Applies to:** Source of Truth v1.1

Owner decisions D1–D9 (from the implementation brief) recorded in plain language, the open items, and
the assumptions made while implementing. These decisions are authoritative and were written into the
Source of Truth v1.1 changelog (**docs/source-of-truth/SOURCE_OF_TRUTH.md**) before any code changed.

## Owner decisions

- **D1 — Pay-at-hotel is the active payment path.** New bookings are created `BookingStatus.Confirmed`
  with `PaymentStatus.Pending`. `BookingStatus.Pending` stays in the enum but is reserved for a future
  online-payment flow; nothing in this work creates `Pending` bookings.
- **D2 — Prices are per night.** `RoomType.BasePrice` holds the nightly NGN price (Standard 30000,
  Deluxe 40000, Executive 55000). The rate is snapshotted into `BookingRoomType.RatePerNight` at
  booking time.
- **D3 — Room statuses stay as the SoT defines them:** Available, Occupied, Maintenance, Inactive.
  No `Reserved`, `Cleaning`, `Expired` or `NoShow` anywhere.
- **D4 — Wave order is 0, 1, 3, 4, 2, 5** (Reception before Admin before Customer self-service before
  Reporting); the tracker's WAVES section reflects this.
- **D5 — Wave 1 is split** into 1a room types, 1b availability search, 1c booking create, 1d booking lookup.
- **D6 — Settings read through `ISettingsProvider`** backed by appsettings (max rooms per booking = 20,
  tax/service charge % = 0 → no tax is computed at all per the amendment, check-in time 14:00,
  check-out time 12:00, cancellation window from `Cancellation:WindowHours`). DB-backed settings arrive
  in Wave 4 without changing callers.
- **D7 — Email uses Google SMTP for now** behind `IEmailSender`; details in the Wave-1 document
  (`docs/local-setup.md`).
- **D8 — No Serilog.** Built-in `ILogger` only; the app runs locally for the time being.
- **D9 — Conventions:** money is `decimal(18,2)`, stay dates are `DateOnly`, business time zone is
  Africa/Lagos, timestamps stored in UTC, booking create is idempotent.

## Deriving decisions (from the amendment / SoT v1.1)

- **No taxes or fees.** `TotalAmount` = Σ line subtotals. `TaxPercentage` was dropped from the settings
  provider accordingly.
- **Booking ↔ user link** is via `Guest.UserId` (nullable); `Booking` has no `UserId`. Anonymous
  bookings never attach to a registered user's guest by email match.
- **Notifications** are created only when the booking's guest has a `UserId`; anonymous guests get the
  confirmation email only.
- **ActivityLog for anonymous actions** stores a null `UserId` and the booking reference in `Description`
  (the `IActivityLogger` already accepts a nullable actor id).
- **Booking reference** format is `DHI-<yyyy>-<XXXXXX>` (Africa/Lagos year; 6 chars from an
  unambiguous alphabet without `0 O 1 I L`), regenerated on unique-index collision.
- **Concurrency approach chosen:** one transaction with `SELECT … FOR UPDATE` row locks on the requested
  `RoomType` rows in ascending `Id` order, then availability check, then insert. Chosen over
  Serializable-with-retry because it is deterministic and easy to test. Rationale recorded in SoT 7c.
- **RoomType display fields** `ImageUrls` (≤ 8) and `Features` (≤ 20) added as `text[]` columns and
  exposed in the room-type DTOs; validation enforces absolute http/https URLs and non-empty trimmed
  features. Image uploads supported via Cloudinary (admin-only). Max 10MB per image; drag-and-drop and file picker; up to 8 images per room type.

## Still open (explicitly not implemented, not hardcoded)

- Expiry of unpaid/held bookings (no expiry job, no background worker).
- Cancellation window and refund rules (existing `Cancellation:WindowHours` config key only).
- Check-in payment rule and check-out outstanding-balance rule.
- Online payment gateway.
- The full room catalogue beyond the three seeded room types.
- No-show status, guest data retention.

## Assumptions made

1. The tracker's local branch note (feature/reconcile-source-of-truth) takes precedence: Phase A is
   worked on a recreated `feature/reconcile-source-of-truth` branch from the current `main`, because
   the original branch was merged and deleted in an earlier round.
2. Npgsql maps `List<string>` to a native `text[]` column. A first attempt forced `jsonb`, which
   requires Npgsql `EnableDynamicJson()`; using `text[]` avoids that dependency. Schema stays a
   primitive collection either way.
3. `dotnet ef database drop` + `database update` is treated as an acceptable "drop and recreate" for
   `deutchezz_dev` (psql is not on PATH; `dotnet ef` uses the project's connection string).
4. The projects's own test DB for integration tests will be `deutchezz_test` on the local PostgreSQL 18
   instance (no Docker on this machine), recreated per test run, per Phase B7.
5. Seeding only inserts missing rows and never updates existing ones, so admin edits survive restarts.
6. Deferred/deleted items the frontend prototype audit asked for but are NOT in the SoT (and therefore
   rejected or deferred, scope guard §9): availability quote endpoint, GET booking lookup, DELETE
   endpoints, folio, refunds, invoice, loyalty, confirm endpoint, PATCH booking, contact form.

## Deviations from the brief

- The brief's original A3 proposed `Booking.Subtotal`/`TaxAmount`/`UserId`; the owner amendment removed
  them. Followed the amendment (SoT v1.1 wins).
- A3's original tax line dropped per the same amendment (no taxes/fees anywhere).