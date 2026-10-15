using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deutchezz.Infrastructure.Data.Configurations;

public class BookingRoomTypeConfiguration : IEntityTypeConfiguration<BookingRoomType>
{
    public void Configure(EntityTypeBuilder<BookingRoomType> builder)
    {
        builder.ToTable("BookingRoomTypes");
        builder.HasKey(brt => brt.Id);
        builder.Property(brt => brt.RatePerNight).HasColumnType("decimal(18,2)");
        builder.Property(brt => brt.Subtotal).HasColumnType("decimal(18,2)");
        builder.Property(brt => brt.ExtraCharge).HasColumnType("decimal(18,2)");
        builder.Property(brt => brt.SurchargeTier).HasMaxLength(32);
        builder.HasIndex(brt => new { brt.BookingId, brt.RoomTypeId }).IsUnique();
        builder.HasIndex(brt => brt.RoomTypeId);
    }
}