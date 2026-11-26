using DocOrganizer.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOrganizer.Api.Data.Configurations;

public sealed class DocumentTypeConfiguration : IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<DocumentType> entity)
    {
        entity.ToTable("DocumentTypes", t => t.HasCheckConstraint(
            "CK_DocumentTypes_DefaultReminderDays",
            "[DefaultReminderDays] BETWEEN 1 AND 365"));

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Name)
              .HasMaxLength(100)
              .IsRequired();

        entity.HasIndex(x => x.Name)
              .IsUnique();

        // Сталий довідник — заповнюється разом із міграцією (InsertData).
        entity.HasData(
            new DocumentType { Id = DocumentTypeIds.IdCard, Name = "ID-картка", DefaultReminderDays = 60 },
            new DocumentType { Id = DocumentTypeIds.ForeignPassport, Name = "Закордонний паспорт", DefaultReminderDays = 90 },
            new DocumentType { Id = DocumentTypeIds.Visa, Name = "Віза", DefaultReminderDays = 30 },
            new DocumentType { Id = DocumentTypeIds.DriverLicense, Name = "Посвідчення водія", DefaultReminderDays = 30 },
            new DocumentType { Id = DocumentTypeIds.MedicalInsurance, Name = "Медичне страхування", DefaultReminderDays = 14 },
            new DocumentType { Id = DocumentTypeIds.CarInsurance, Name = "Автоцивілка (ОСЦПВ)", DefaultReminderDays = 14 },
            new DocumentType { Id = DocumentTypeIds.Certificate, Name = "Сертифікат", DefaultReminderDays = 30 },
            new DocumentType { Id = DocumentTypeIds.Contract, Name = "Договір", DefaultReminderDays = 30 });
    }
}