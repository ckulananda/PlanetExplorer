using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PlanetExplorer
{
    public partial class QuizWindow : Window
    {
        public List<QuizAttempt> Attempts { get; } = new();

        public class QuizAttempt
        {
            public int QuestionId { get; set; }
            public int SelectedIndex { get; set; }
            public bool IsCorrect { get; set; }
        }

        private readonly List<QuizQuestion> _questions;
        private readonly List<int?> _answers;
        private int _index = 0;

        public int FinalScore { get; private set; } = 0;
        public int TotalQuestions => _questions.Count;

        public QuizWindow(int planetId, string planetName, List<QuizQuestion> questions)
        {
            InitializeComponent();

            _questions = ValidateAndSanitizeQuestions(questions);

            if (_questions.Count == 0)
            {
                MessageBox.Show(
                    "This quiz is not available right now because no valid questions were found.",
                    "Quiz Unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = false;
                Close();
                return;
            }

            _answers = Enumerable.Repeat<int?>(null, _questions.Count).ToList();

            string displayPlanet = string.IsNullOrWhiteSpace(planetName)
                ? "Unknown Planet"
                : planetName.Trim();

            TitleText.Text = $"Quiz: {displayPlanet}";

            ShowQuestion();
            UpdateUIState();
        }

        private List<QuizQuestion> ValidateAndSanitizeQuestions(List<QuizQuestion> questions)
        {
            var validQuestions = new List<QuizQuestion>();

            if (questions == null)
                return validQuestions;

            foreach (var q in questions)
            {
                if (q == null)
                    continue;

                if (string.IsNullOrWhiteSpace(q.QuestionText))
                    continue;

                if (q.Options == null)
                    continue;

                var cleanedOptions = q.Options
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Select(o => o.Trim())
                    .ToList();

                if (cleanedOptions.Count < 2)
                    continue;

                if (q.CorrectIndex < 0 || q.CorrectIndex >= cleanedOptions.Count)
                    continue;

                validQuestions.Add(new QuizQuestion
                {
                    QuestionId = q.QuestionId,
                    QuestionText = q.QuestionText.Trim(),
                    Options = cleanedOptions,
                    CorrectIndex = q.CorrectIndex
                });
            }

            return validQuestions;
        }

        private void ShowQuestion()
        {
            if (_questions.Count == 0)
                return;

            if (_index < 0 || _index >= _questions.Count)
                return;

            var q = _questions[_index];

            QuestionText.Text = $"Q{_index + 1}. {q.QuestionText}";

            Opt0.Content = GetOptionText(q, 0);
            Opt1.Content = GetOptionText(q, 1);
            Opt2.Content = GetOptionText(q, 2);
            Opt3.Content = GetOptionText(q, 3);

            Opt0.Visibility = q.Options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Opt1.Visibility = q.Options.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Opt2.Visibility = q.Options.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
            Opt3.Visibility = q.Options.Count > 3 ? Visibility.Visible : Visibility.Collapsed;

            var saved = _answers[_index];
            Opt0.IsChecked = saved == 0;
            Opt1.IsChecked = saved == 1;
            Opt2.IsChecked = saved == 2;
            Opt3.IsChecked = saved == 3;

            ProgressText.Text = $"Question {_index + 1} of {_questions.Count}";
        }

        private string GetOptionText(QuizQuestion question, int index)
        {
            if (question?.Options == null)
                return string.Empty;

            return question.Options.ElementAtOrDefault(index) ?? string.Empty;
        }

        private int? GetSelectedIndex()
        {
            if (Opt0.Visibility == Visibility.Visible && Opt0.IsChecked == true) return 0;
            if (Opt1.Visibility == Visibility.Visible && Opt1.IsChecked == true) return 1;
            if (Opt2.Visibility == Visibility.Visible && Opt2.IsChecked == true) return 2;
            if (Opt3.Visibility == Visibility.Visible && Opt3.IsChecked == true) return 3;
            return null;
        }

        private bool SaveCurrentAnswer(bool requireSelection = false)
        {
            var selectedIndex = GetSelectedIndex();

            if (requireSelection && selectedIndex == null)
            {
                MessageBox.Show(
                    "Please choose an answer before continuing.",
                    "Answer Needed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return false;
            }

            if (selectedIndex != null)
            {
                var currentQuestion = _questions[_index];

                if (selectedIndex < 0 || selectedIndex >= currentQuestion.Options.Count)
                {
                    MessageBox.Show(
                        "Please choose a valid answer option.",
                        "Invalid Answer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return false;
                }
            }

            _answers[_index] = selectedIndex;
            return true;
        }

        private void UpdateUIState()
        {
            BackButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _questions.Count - 1;
            SubmitButton.IsEnabled = _index == _questions.Count - 1;
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveCurrentAnswer(requireSelection: true))
                return;

            if (_index >= _questions.Count - 1)
                return;

            _index++;
            ShowQuestion();
            UpdateUIState();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveCurrentAnswer())
                return;

            if (_index <= 0)
                return;

            _index--;
            ShowQuestion();
            UpdateUIState();
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveCurrentAnswer(requireSelection: true))
                return;

            if (_questions.Count == 0)
            {
                MessageBox.Show(
                    "There are no questions available to submit.",
                    "Nothing to Submit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_answers.Any(a => a == null))
            {
                int unansweredCount = _answers.Count(a => a == null);

                var result = MessageBox.Show(
                    $"You still have {unansweredCount} unanswered question(s).\n\nDo you want to submit the quiz now?",
                    "Unanswered Questions",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                    return;
            }

            Attempts.Clear();
            int score = 0;

            for (int i = 0; i < _questions.Count; i++)
            {
                var question = _questions[i];
                var selectedIndex = _answers[i];

                if (selectedIndex == null)
                    continue;

                if (selectedIndex.Value < 0 || selectedIndex.Value >= question.Options.Count)
                    continue;

                bool isCorrect = selectedIndex.Value == question.CorrectIndex;

                Attempts.Add(new QuizAttempt
                {
                    QuestionId = question.QuestionId,
                    SelectedIndex = selectedIndex.Value,
                    IsCorrect = isCorrect
                });

                if (isCorrect)
                    score++;
            }

            FinalScore = score;

            MessageBox.Show(
                $"You completed the quiz.\n\nYour score: {FinalScore} out of {_questions.Count}",
                "Quiz Completed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
    }
}