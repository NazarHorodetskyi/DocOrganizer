using DocOrganizer.DataLayer.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOrganizer.DataLayer.Data.Configurations;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> entity)
    {
        entity.ToTable("Reminders", t => t.HasCheckConstraint(
            "CK_Reminders_DaysBefore",
            "[DaysBefore] BETWEEN 0 AND 365"));

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Channel)
              .HasConversion<string>()
              .HasMaxLength(10)
              .IsRequired();

        // Для одного документа — не більше одного нагадування з тим самим відступом.
        // Індекс також покриває FK DocumentId.
        entity.HasIndex(x => new { x.DocumentId, x.DaysBefore })
              .IsUnique();

        // Пошук нагадувань, які пора надіслати: WHERE IsSent = 0 AND RemindOn <= @today.
        entity.HasIndex(x => new { x.IsSent, x.RemindOn });

        // Нагадування без документа не мають сенсу — видаляються разом із ним.
        entity.HasOne(x => x.Document)
              .WithMany(x => x.Reminders)
              .HasForeignKey(x => x.DocumentId)
              .OnDelete(DeleteBehavior.Cascade);
    }
}