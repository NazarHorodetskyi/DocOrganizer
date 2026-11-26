using DocOrganizer.DataLayer.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocOrganizer.DataLayer.Data.Configurations;

public sealed class DocumentTagConfiguration : IEntityTypeConfiguration<DocumentTag>
{
    public void Configure(EntityTypeBuilder<DocumentTag> entity)
    {
        entity.ToTable("DocumentTags");

        // Складений первинний ключ проміжної сутності: один тег на документі — один раз.
        entity.HasKey(x => new { x.DocumentId, x.TagId });

        entity.Property(x => x.AddedAt)
              .HasDefaultValueSql("SYSUTCDATETIME()");

        // Видалення документа прибирає його мітки.
        entity.HasOne(x => x.Document)
              .WithMany(x => x.DocumentTags)
              .HasForeignKey(x => x.DocumentId)
              .OnDelete(DeleteBehavior.Cascade);

        // Видалення тега прибирає лише мітки, документи залишаються.
        entity.HasOne(x => x.Tag)
              .WithMany(x => x.DocumentTags)
              .HasForeignKey(x => x.TagId)
              .OnDelete(DeleteBehavior.Cascade);
    }
}