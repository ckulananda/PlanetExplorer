using System;

namespace PlanetExplorer
{
    public class QuizAnswerLog
    {
        public int QuizAnswerLogId { get; set; }

        public int UserId { get; set; }
        public int? PlanetId { get; set; }
        public int? ItemId { get; set; }

        public int QuizQuestionEntityId { get; set; }

        public int SelectedIndex { get; set; } // 0..3
        public bool IsCorrect { get; set; }

        public DateTime Timestamp { get; set; }
        public Guid? SessionId { get; set; }
    }
}
