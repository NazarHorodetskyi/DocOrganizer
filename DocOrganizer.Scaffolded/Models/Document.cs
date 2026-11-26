using System;
using System.Collections.Generic;

namespace DocOrganizer.Scaffolded.Models;

public partial class Document
{
    public int Id { get; set; }

    public int OwnerId { get; set; }

    public int DocumentTypeId { get; set; }

    public string Number { get; set; } = null!;

    public string Title { get; set; } = null!;

    public DateOnly IssueDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public string? FilePath { get; set; }

    public string? Notes { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public byte[] Version { get; set; } = null!;

    public string? IssuedBy { get; set; }

    public virtual ICollection<DocumentTag> DocumentTags { get; set; } = new List<DocumentTag>();

    public virtual DocumentType DocumentType { get; set; } = null!;

    public virtual Owner Owner { get; set; } = null!;

    public virtual ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();
}
