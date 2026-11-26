using DocOrganizer.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOrganizer.Api.Data.Configurations;

public sealed class OwnerConfiguration : IEntityTypeConfiguration<Owner>
{
    public void Configure(EntityTypeBuilder<Owner> entity)
    {
        entity.ToTable("Owners");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.FullName)
              .HasMaxLength(150)
              .IsRequired();

        entity.Property(x => x.Email)
              .HasMaxLength(256)
              .IsRequired();

        // Бізнес-ідентифікатор власника: один email — один власник.
        entity.HasIndex(x => x.Email)
              .IsUnique();

        entity.HasIndex(x => x.FullName);

        // Ідентифікатор користувача OIDC (claim sub). Індекс — бо КОЖЕН запит
        // звичайного користувача фільтрується за AccountId (resource-based доступ).
        entity.Property(x => x.AccountId)
              .HasMaxLength(100)
              .IsRequired();

        entity.HasIndex(x => x.AccountId);
    }
}