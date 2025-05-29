using System;

namespace deRemind.Models
{
    public class Reminder
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime ReminderDateTime { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsRepeating { get; set; }
        public TimeSpan RepeatInterval { get; set; }
    }
}