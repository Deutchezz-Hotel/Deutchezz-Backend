# Deutchezz Hotel Ispahan - Backend Structure Audit

## Overview
Clean Architecture pattern implemented following the Source of Truth v1.0 specification.

## Domain Layer (src/Domain/)
- BaseEntity/Common/: BaseEntity, AuditableEntity, SoftDeletableEntity
- Entities/: User, Role, Guest, RoomType, Room, Booking, BookingRoomType, RoomAssignment, Payment, Notification, ActivityLog
- Enums/: UserRole, RoomStatus, BookingStatus, PaymentStatus, PaymentMethod, NotificationType

## Application Layer (src/Application/)
- DTOs/: Auth, Availability, Booking, Rooms, RoomTypes, Payments, Profile, Notifications, Reports

## Infrastructure Layer (src/Infrastructure/)
- Persistence/: ApplicationDbContext, Extensions/ModelBuilderExtensions

## Presentation Layer (src/Presentation/)
- Controllers/: Auth, RoomTypes, Availability, Bookings; Me/, Reception/, Admin/

## Key Design Decisions
- Identity integrated (User extends IdentityUser<Guid>)
- Soft delete with global query filter
- Audit timestamps handled in DbContext
- Rate snapshots preserved in BookingRoomType
- Separation of reservation vs physical room assignment
- Anonymous bookings supported (Guest.UserId nullable)
