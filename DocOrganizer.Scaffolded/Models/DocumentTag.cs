using System;
using System.Collections.Generic;

namespace DocOrganizer.Scaffolded.Models;

public partial class DocumentTag
{
    public int DocumentId { get; set; }

    public int TagId { get; set; }

    public DateTime AddedAt { get; set; }

    public virtual Document Document { get; set; } = null!;

    public virtual Tag Tag { get; set; } = null!;
}
