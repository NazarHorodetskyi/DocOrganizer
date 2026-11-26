using DocOrganizer.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOrganizer.Api.Data.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> entity)
    {
        entity.ToTable("Tags");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Name)
              .HasMaxLength(50)
              .IsRequired();

        entity.HasIndex(x => x.Name)
              .IsUnique();

        entity.HasData(
            new Tag { Id = TagIds.Personal, Name = "Особисте" },
            new Tag { Id = TagIds.Travel, Name = "Подорожі" },
            new Tag { Id = TagIds.Car, Name = "Авто" },
            new Tag { Id = TagIds.Work, Name = "Робота" },
            new Tag { Id = TagIds.Finance, Name = "Фінанси" },
            new Tag { Id = TagIds.Urgent, Name = "Терміново" });
    }
}