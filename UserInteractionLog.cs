using System;
using System.ComponentModel.DataAnnotations;

namespace PlanetExplorer
{
    public class UserInteractionLog
    {
        [Key]
        public int LogId { get; set; }

        public int PlanetId { get; set; }

        public string ActionType { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.Now;

        // ✅ New BIS fields
        public Guid SessionId { get; set; }

        public double? DurationSeconds { get; set; }

        public string? Meta { get; set; }
       

    }
}
