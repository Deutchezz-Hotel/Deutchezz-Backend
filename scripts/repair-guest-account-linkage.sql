-- One-off repair for bookings created before account linkage existed.
--
-- Before the fix, a guest checkout whose email already belonged to a registered account
-- created (or reused) an anonymous guest row with "UserId" = NULL. Those bookings never
-- appeared in the customer's dashboard, and the reused guest could carry a stale name.
--
-- This script links every orphaned anonymous guest to the account that owns the same email,
-- adopts the account's name, and re-points its notifications to the account. It is idempotent
-- and safe to run multiple times.
--
-- Usage:
--   psql -U postgres -d deutchezz_dev -f scripts/repair-guest-account-linkage.sql

BEGIN;

-- 1. Link anonymous guests to a matching registered account and adopt the account name.
UPDATE "Guests" g
SET "UserId"    = u."Id",
    "FirstName" = COALESCE(NULLIF(btrim(u."FirstName"), ''), g."FirstName"),
    "LastName"  = COALESCE(NULLIF(btrim(u."LastName"), ''),  g."LastName"),
    "UpdatedAt" = now()
FROM "Users" u
WHERE g."UserId" IS NULL
  AND u."Email" IS NOT NULL
  AND lower(btrim(g."Email")) = lower(btrim(u."Email"));

-- 2. Attribute notifications created anonymously to the now-linked account.
UPDATE "Notifications" n
SET "UserId" = g."UserId"
FROM "Guests" g
WHERE n."GuestId" = g."Id"
  AND n."UserId" IS NULL
  AND g."UserId" IS NOT NULL;

COMMIT;
