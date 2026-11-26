using System;
using System.Collections.Generic;

namespace DocOrganizer.Scaffolded.Models;

public partial class Reminder
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    public int DaysBefore { get; set; }

    public DateOnly RemindOn { get; set; }

    public string Channel { get; set; } = null!;

    public bool IsSent { get; set; }

    public DateTime? SentAt { get; set; }

    public virtual Document Document { get; set; } = null!;
}
