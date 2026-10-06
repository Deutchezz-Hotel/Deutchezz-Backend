using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deutchezz.Infrastructure.Data.Configurations;

public class BookingRoomTypeConfiguration : IEntityTypeConfiguration<BookingRoomType>
{
    public void Configure(EntityTypeBuilder<BookingRoomType> builder)
    {
        builder.ToTable("BookingRoomTypes");
        builder.HasKey(brt => new { brt.BookingId, brt.RoomTypeId });
        builder.Property(brt => brt.NightlyRateSnapshot).HasColumnType("decimal(18,2)");
        builder.Property(brt => brt.LineTotal).HasColumnType("decimal(18,2)");
        builder.HasIndex(brt => brt.RoomTypeId);
    }
}