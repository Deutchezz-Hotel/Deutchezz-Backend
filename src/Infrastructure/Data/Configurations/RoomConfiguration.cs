using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deutchezz.Infrastructure.Data.Configurations;

public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("Rooms");
        builder.Property(r => r.RoomNumber).IsRequired();
        builder.HasIndex(r => r.RoomNumber).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.Property(r => r.Status).HasConversion<string>();
    }
}