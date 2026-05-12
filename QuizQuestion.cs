using System.Collections.Generic;

namespace PlanetExplorer
{
    public class QuizQuestion
    {
        public int QuestionId { get; set; }      // ✅ add this
        public string QuestionText { get; set; }
        public List<string> Options { get; set; } = new();
        public int CorrectIndex { get; set; }
    }
}
