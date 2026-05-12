using HelixToolkit.Wpf;
using System;
using System.ComponentModel.DataAnnotations;

namespace PlanetExplorer
{
    public class QuizResult
    {
        [Key]
        public int ResultId { get; set; }

        // Old (keep if you still use planets table)
        public int? PlanetId { get; set; }

        // ✅ New (SpaceItems)
        public int? ItemId { get; set; }

        public int Score { get; set; }
        public int TotalQuestions { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public Guid? SessionId { get; set; }
        public int? UserId { get; set; }



    }
}
