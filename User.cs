using System;
using System.ComponentModel.DataAnnotations;

namespace PlanetExplorer
{
    public class User
    {
        [Key]
        public int UserId { get; set; }

        public string FullName { get; set; } = "";
        public int? Age { get; set; }
        public string? Location { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? JobType { get; set; }

        // saved path to Propic_<UserId>.<ext>
        public string? ProfileImagePath { get; set; }

        // ✅ for soft delete
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
