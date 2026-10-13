using Deutchezz.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deutchezz.Infrastructure.Data.Configurations;

public class PendingRegistrationConfiguration : IEntityTypeConfiguration<PendingRegistration>
{
    public void Configure(EntityTypeBuilder<PendingRegistration> builder)
    {
        builder.ToTable("PendingRegistrations");

        builder.Property(p => p.Email).HasMaxLength(256).IsRequired();
        builder.Property(p => p.FirstName).HasMaxLength(100);
        builder.Property(p => p.LastName).HasMaxLength(100);
        builder.Property(p => p.PhoneNumber).HasMaxLength(20);
        builder.Property(p => p.PasswordHash).IsRequired();
        builder.Property(p => p.CodeHash).HasMaxLength(128).IsRequired();

        builder.HasIndex(p => p.Email).IsUnique();
        builder.HasIndex(p => p.ExpiresAt);
    }
}
