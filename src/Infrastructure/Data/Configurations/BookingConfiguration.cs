using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deutchezz.Infrastructure.Data.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.Property(b => b.BookingReference).IsRequired().HasMaxLength(32);
        builder.HasIndex(b => b.BookingReference).IsUnique();
        builder.Property(b => b.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.Status).HasConversion<string>();
        builder.Property(b => b.PaymentStatus).HasConversion<string>();
        builder.Property(b => b.Source).HasConversion<string>();
        builder.Property(b => b.IdempotencyKey).HasMaxLength(100);
        builder.HasIndex(b => b.IdempotencyKey).IsUnique();
        builder.Property(b => b.IdempotencyRequestHash).HasMaxLength(64);
        builder.Property(b => b.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(b => new { b.CheckInDate, b.CheckOutDate, b.Status });
    }
}