using DocOrganizer.DataLayer.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOrganizer.DataLayer.Data.Configurations;

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> entity)
    {
        // CHECK: документ не може завершитися раніше, ніж його видали.
        entity.ToTable("Documents", t => t.HasCheckConstraint(
            "CK_Documents_ExpiryAfterIssue",
            "[ExpiryDate] IS NULL OR [ExpiryDate] >= [IssueDate]"));

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Number).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
        entity.Property(x => x.FilePath).HasMaxLength(500);
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.Property(x => x.IssuedBy).HasMaxLength(200);

        // Enum зберігаємо рядком: у SQL видно 'Active', а не 0.
        entity.Property(x => x.Status)
              .HasConversion<string>()
              .HasMaxLength(20)
              .IsRequired();

        entity.Property(x => x.CreatedAt)
              .HasDefaultValueSql("SYSUTCDATETIME()");

        // Concurrency token: SQL Server сам змінює значення при кожному UPDATE.
        entity.Property(x => x.Version)
              .IsRowVersion();

        // Бізнес-ідентифікатор: номер унікальний у межах типу документа.
        // Індекс також покриває FK DocumentTypeId (окремий індекс EF не створить).
        entity.HasIndex(x => new { x.DocumentTypeId, x.Number })
              .IsUnique();

        // Календар завершення строків: пошук і сортування за датою.
        entity.HasIndex(x => x.ExpiryDate);

        // Фільтр «документи власника за статусом».
        entity.HasIndex(x => new { x.OwnerId, x.Status });

        // Власника не можна видалити, поки в нього є документи (юридично важливі дані).
        entity.HasOne(x => x.Owner)
              .WithMany(x => x.Documents)
              .HasForeignKey(x => x.OwnerId)
              .OnDelete(DeleteBehavior.Restrict);

        // Тип з довідника не можна видалити, поки він використовується.
        entity.HasOne(x => x.DocumentType)
              .WithMany(x => x.Documents)
              .HasForeignKey(x => x.DocumentTypeId)
              .OnDelete(DeleteBehavior.Restrict);
    }
}