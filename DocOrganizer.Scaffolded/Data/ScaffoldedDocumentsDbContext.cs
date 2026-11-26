using System;
using System.Collections.Generic;
using DocOrganizer.Scaffolded.Models;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.Scaffolded.Data;

public partial class ScaffoldedDocumentsDbContext : DbContext
{
    public ScaffoldedDocumentsDbContext(DbContextOptions<ScaffoldedDocumentsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentTag> DocumentTags { get; set; }

    public virtual DbSet<DocumentType> DocumentTypes { get; set; }

    public virtual DbSet<Owner> Owners { get; set; }

    public virtual DbSet<Reminder> Reminders { get; set; }

    public virtual DbSet<Tag> Tags { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasIndex(e => new { e.DocumentTypeId, e.Number }, "IX_Documents_DocumentTypeId_Number").IsUnique();

            entity.HasIndex(e => e.ExpiryDate, "IX_Documents_ExpiryDate");

            entity.HasIndex(e => new { e.OwnerId, e.Status }, "IX_Documents_OwnerId_Status");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(sysutcdatetime())");
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.IssuedBy).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.Number).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Version)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.DocumentType).WithMany(p => p.Documents)
                .HasForeignKey(d => d.DocumentTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(d => d.Owner).WithMany(p => p.Documents)
                .HasForeignKey(d => d.OwnerId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<DocumentTag>(entity =>
        {
            entity.HasKey(e => new { e.DocumentId, e.TagId });

            entity.HasIndex(e => e.TagId, "IX_DocumentTags_TagId");

            entity.Property(e => e.AddedAt).HasDefaultValueSql("(sysutcdatetime())");

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentTags).HasForeignKey(d => d.DocumentId);

            entity.HasOne(d => d.Tag).WithMany(p => p.DocumentTags).HasForeignKey(d => d.TagId);
        });

        modelBuilder.Entity<DocumentType>(entity =>
        {
            entity.HasIndex(e => e.Name, "IX_DocumentTypes_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Owner>(entity =>
        {
            entity.HasIndex(e => e.Email, "IX_Owners_Email").IsUnique();

            entity.HasIndex(e => e.FullName, "IX_Owners_FullName");

            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.FullName).HasMaxLength(150);
        });

        modelBuilder.Entity<Reminder>(entity =>
        {
            entity.HasIndex(e => new { e.DocumentId, e.DaysBefore }, "IX_Reminders_DocumentId_DaysBefore").IsUnique();

            entity.HasIndex(e => new { e.IsSent, e.RemindOn }, "IX_Reminders_IsSent_RemindOn");

            entity.Property(e => e.Channel).HasMaxLength(10);

            entity.HasOne(d => d.Document).WithMany(p => p.Reminders).HasForeignKey(d => d.DocumentId);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(e => e.Name, "IX_Tags_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(50);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
