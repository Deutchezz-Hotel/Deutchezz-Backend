using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deutchezz.Infrastructure.Data.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.Property(b => b.BookingReference).IsRequired();
        builder.HasIndex(b => b.BookingReference).IsUnique();
        builder.Property(b => b.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(b => b.Status).HasConversion<string>();
        builder.Property(b => b.PaymentStatus).HasConversion<string>();
        builder.Property(b => b.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(b => new { b.CheckInDate, b.CheckOutDate, b.Status });
    }
}