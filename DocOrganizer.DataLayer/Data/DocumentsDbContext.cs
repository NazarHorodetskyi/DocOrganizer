using DocOrganizer.DataLayer.Domain;
using Microsoft.EntityFrameworkCore;

namespace DocOrganizer.DataLayer.Data;

public sealed class DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : DbContext(options)
{
    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        // Підхоплює всі IEntityTypeConfiguration<T> з папки Data/Configurations.
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentsDbContext).Assembly);
}