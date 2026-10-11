using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deutchezz.Infrastructure.Data.Configurations;

public class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("Settings");
        builder.Property(s => s.Key).IsRequired().HasMaxLength(100);
        builder.HasIndex(s => s.Key).IsUnique();
        builder.Property(s => s.Value).HasMaxLength(4000);
    }
}