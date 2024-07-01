﻿using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace SolexCode.CRM.API.New.Models;

public class Events
{
    public int Id { get; set; }
    public string EventName { get; set; }
    public DateTime Date { get; set; }
    public string Time { get; set; }
    public string Venue { get; set; }

    public string CreatedByName { get; set; }
    public string CreatedByEmail { get; set; }
    public int? CreatedById { get; set; }

  
    public DateTimeOffset ReminderDate { get; set; }
    public string ReminderTime { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.Now;
    public DateTime DateModified { get; set; } = DateTime.Now;

    public ICollection<Participant>? Participants { get; set; }
    public string Description { get; set; }
    public bool IsImportant { get; set; }
    public bool IsSendViaEmail { get; set; }

    [ForeignKey("Id")]
    public int? NewLeadId { get; set; }
    [JsonIgnore]
    [ForeignKey("NewLeadId")]

    public NewLead NewLead { get; set; }
}
