using System;
using System.ComponentModel.DataAnnotations;

namespace PlanetExplorer
{
    public class UserSession
    {
        [Key]
        public Guid SessionId { get; set; }

        public int UserId { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.Now;
    }
}
