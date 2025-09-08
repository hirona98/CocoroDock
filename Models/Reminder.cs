using System;

namespace CocoroDock.Models
{
    public class Reminder
    {
        public int Id { get; set; }
        public string RemindDatetime { get; set; } = string.Empty;
        public string Requirement { get; set; } = string.Empty;
    }

    public enum ReminderStatus
    {
        Pending = 0,
        Completed = 1,
        Failed = 2
    }
}