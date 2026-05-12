using System;
using System.ComponentModel.DataAnnotations;

namespace PlanetExplorer
{
    public class QuizQuestionEntity
    {
        [Key]
        public int QuestionId { get; set; }

        public int? ItemId { get; set; }

        public string TopicType { get; set; } = "General";

        public string QuestionText { get; set; } = "";

        public string OptionA { get; set; } = "";
        public string OptionB { get; set; } = "";
        public string OptionC { get; set; } = "";
        public string OptionD { get; set; } = "";

        public int CorrectIndex { get; set; } // 0..3

        public int? Difficulty { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
