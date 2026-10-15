using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deutchezz.Infrastructure.Data.Configurations;

public class RoomTypeSurchargeTierConfiguration : IEntityTypeConfiguration<RoomTypeSurchargeTier>
{
    public void Configure(EntityTypeBuilder<RoomTypeSurchargeTier> builder)
    {
        builder.ToTable("RoomTypeSurchargeTiers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Amount).HasColumnType("decimal(18,2)");
        builder.HasIndex(t => t.RoomTypeId);
        builder.HasIndex(t => new { t.RoomTypeId, t.FromHours }).IsUnique();

        builder.HasOne<RoomType>()
            .WithMany()
            .HasForeignKey(t => t.RoomTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
