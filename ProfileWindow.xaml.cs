using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

// If you're using EF Core and want the "entity exists" check for UserSession:
// using Microsoft.EntityFrameworkCore;

namespace PlanetExplorer
{
    public partial class ProfileWindow : Window
    {
        // ===================== FIELDS =====================

        private User? _selectedUser;
        private string? _pickedImagePath;     // original selected file
        private string? _previewImagePath;    // current image shown

        private const int MaxNameLength = 100;
        private const int MaxLocationLength = 100;
        private const int MaxPhoneLength = 25;
        private const int MaxEmailLength = 150;
        private const int MaxJobLength = 100;

        // ===================== CTOR =====================

        public ProfileWindow()
        {
            InitializeComponent();

            HookEvents();
            RefreshUsers();
            ClearForm();
        }

        // ===================== EVENT HOOKS =====================

        private void HookEvents()
        {
            UsersList.SelectionChanged += UsersList_SelectionChanged;

            SearchBox.TextChanged += (_, __) => RefreshUsers();
            ShowInactiveCheck.Checked += (_, __) => RefreshUsers();
            ShowInactiveCheck.Unchecked += (_, __) => RefreshUsers();
        }

        // ===================== USERS LIST =====================

        private void RefreshUsers()
        {
            using var db = new PlanetContext();

            bool showInactive = ShowInactiveCheck.IsChecked == true;
            string term = (SearchBox.Text ?? "").Trim();

            var q = db.Users.AsQueryable();

            if (!showInactive)
                q = q.Where(u => u.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
            {
                q = q.Where(u =>
                    u.FullName.Contains(term) ||
                    (u.Email ?? "").Contains(term));
            }

            var list = q.OrderBy(u => u.FullName).ToList();
            UsersList.ItemsSource = list;

            StatusText.Text = $"Loaded {list.Count} users.";
        }

        private void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedUser = UsersList.SelectedItem as User;
            if (_selectedUser == null) return;

            LoadToForm(_selectedUser);

            LoadQuizStats(_selectedUser.UserId);
            LoadWrongQuestionStats(_selectedUser.UserId);

            StatusText.Text = $"Editing: {_selectedUser.FullName} (ID {_selectedUser.UserId})";
        }

        // ===================== QUIZ STATS =====================

        private void LoadQuizStats(int userId)
        {
            using var db = new PlanetContext();

            var planetNames = db.Planets
                .ToList()
                .GroupBy(p => p.PlanetId)
                .ToDictionary(g => g.Key, g => g.First().Name);

            var results = db.QuizResults
                .Where(r => r.UserId == userId)
                .OrderBy(r => r.Timestamp)
                .ToList();

            if (results.Count == 0)
            {
                StatSummaryText.Text = "No quiz attempts yet.";
                QuizHistoryList.ItemsSource = null;

                BestStreakText.Text = "0";
                LastQuizText.Text = "—";
                return;
            }

            static double Percent(int score, int total)
                => total <= 0 ? 0.0 : (double)score / total * 100.0;

            int totalQuizzes = results.Count;
            double avg = results.Average(r => Percent(r.Score, r.TotalQuestions));
            double best = results.Max(r => Percent(r.Score, r.TotalQuestions));

            StatSummaryText.Text =
                $"Total Quizzes: {totalQuizzes}    |    " +
                $"Average: {avg:F1}%    |    " +
                $"Best: {best:F1}%";

            var last = results.OrderByDescending(r => r.Timestamp).First();
            string lastPlanet =
                last.PlanetId.HasValue && planetNames.TryGetValue(last.PlanetId.Value, out var pn)
                    ? pn
                    : "Unknown";

            LastQuizText.Text = $"{last.Timestamp:g}  ({lastPlanet})";

            const double streakThreshold = 80.0;
            int bestStreak = 0;
            int currentStreak = 0;

            foreach (var r in results.OrderBy(r => r.Timestamp))
            {
                double pct = Percent(r.Score, r.TotalQuestions);

                if (pct >= streakThreshold)
                {
                    currentStreak++;
                    if (currentStreak > bestStreak) bestStreak = currentStreak;
                }
                else
                {
                    currentStreak = 0;
                }
            }

            BestStreakText.Text = $"{bestStreak} (≥{streakThreshold:0}% runs)";

            var resultsDesc = results.OrderByDescending(r => r.Timestamp).ToList();

            QuizHistoryList.ItemsSource = resultsDesc.Select(r =>
            {
                string planetName =
                    r.PlanetId.HasValue && planetNames.TryGetValue(r.PlanetId.Value, out var name)
                        ? name
                        : "Unknown";

                return $"{planetName} | Score: {r.Score}/{r.TotalQuestions} | {r.Timestamp:g}";
            }).ToList();
        }

        // ===================== WRONG QUESTION STATS =====================

        private void LoadWrongQuestionStats(int userId)
        {
            using var db = new PlanetContext();

            var wrongStats = db.QuizAnswerLogs
                .Where(x => x.UserId == userId && !x.IsCorrect)
                .GroupBy(x => x.QuizQuestionEntityId)
                .Select(g => new
                {
                    QuestionId = g.Key,
                    WrongCount = g.Count()
                })
                .OrderByDescending(x => x.WrongCount)
                .Take(20)
                .ToList();

            if (wrongStats.Count == 0)
            {
                WrongQuestionsList.ItemsSource = new List<string> { "No wrong answers logged yet." };
                return;
            }

            var ids = wrongStats.Select(w => w.QuestionId).ToHashSet();

            var questionTexts = db.QuizQuestions
                .Where(q => ids.Contains(q.QuestionId))
                .ToList()
                .GroupBy(q => q.QuestionId)
                .ToDictionary(g => g.Key, g => g.First().QuestionText);

            WrongQuestionsList.ItemsSource = wrongStats.Select(x =>
            {
                string text = questionTexts.TryGetValue(x.QuestionId, out var qt)
                    ? qt
                    : "Unknown Question";

                return $"{text}  |  Wrong: {x.WrongCount}";
            }).ToList();
        }

        // ===================== FORM LOAD/CLEAR =====================

        private void LoadToForm(User u)
        {
            NameBox.Text = u.FullName;
            AgeBox.Text = u.Age?.ToString() ?? "";
            LocationBox.Text = u.Location ?? "";
            PhoneBox.Text = u.Phone ?? "";
            EmailBox.Text = u.Email ?? "";
            JobBox.Text = u.JobType ?? "";
            IsActiveCheck.IsChecked = u.IsActive;

            _pickedImagePath = null;
            _previewImagePath = u.ProfileImagePath;

            LoadPreviewImage(_previewImagePath);
            PicHintText.Text = string.IsNullOrWhiteSpace(_previewImagePath) ? "No picture selected" : "Profile picture loaded";
        }

        private void ClearForm()
        {
            _selectedUser = null;
            UsersList.SelectedIndex = -1;

            NameBox.Text = "";
            AgeBox.Text = "";
            LocationBox.Text = "";
            PhoneBox.Text = "";
            EmailBox.Text = "";
            JobBox.Text = "";
            IsActiveCheck.IsChecked = true;

            _pickedImagePath = null;
            _previewImagePath = null;
            LoadPreviewImage(null);

            StatSummaryText.Text = "No user selected.";
            BestStreakText.Text = "—";
            LastQuizText.Text = "—";
            QuizHistoryList.ItemsSource = null;
            WrongQuestionsList.ItemsSource = null;

            StatusText.Text = "Ready.";
        }

        // ===================== BUTTONS =====================

        private void NewUser_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            StatusText.Text = "New user.";
        }

        private void ChoosePicture_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.webp",
                Title = "Select profile picture"
            };

            if (dlg.ShowDialog() == true)
            {
                if (!ValidateImageFile(dlg.FileName, out string imageError))
                {
                    MessageBox.Show(imageError, "Invalid Picture", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _pickedImagePath = dlg.FileName;
                _previewImagePath = dlg.FileName;

                LoadPreviewImage(_previewImagePath);
                PicHintText.Text = "Preview selected (not saved yet)";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var db = new PlanetContext();

                var validation = ValidateForm(db);
                if (!validation.IsValid)
                {
                    MessageBox.Show(
                        validation.ErrorMessage,
                        "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var data = validation.Data!.Value;

                if (_selectedUser == null)
                {
                    var u = new User
                    {
                        FullName = data.FullName,
                        Age = data.Age,
                        Location = data.Location,
                        Phone = data.Phone,
                        Email = data.Email,
                        JobType = data.JobType,
                        IsActive = data.IsActive
                    };

                    db.Users.Add(u);
                    db.SaveChanges();

                    if (!string.IsNullOrWhiteSpace(_pickedImagePath) && File.Exists(_pickedImagePath))
                    {
                        u.ProfileImagePath = SaveProfilePictureToLocalFolder(u.UserId, _pickedImagePath);
                        db.SaveChanges();

                        _previewImagePath = u.ProfileImagePath;
                        LoadPreviewImage(_previewImagePath);
                        PicHintText.Text = "Saved";
                    }

                    StatusText.Text = $"Created user ID {u.UserId}.";
                    RefreshUsers();
                    SelectUserInList(u.UserId);

                    LoadQuizStats(u.UserId);
                    LoadWrongQuestionStats(u.UserId);
                    return;
                }

                var row = db.Users.FirstOrDefault(x => x.UserId == _selectedUser.UserId);
                if (row == null)
                {
                    MessageBox.Show("User not found.", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                row.FullName = data.FullName;
                row.Age = data.Age;
                row.Location = data.Location;
                row.Phone = data.Phone;
                row.Email = data.Email;
                row.JobType = data.JobType;
                row.IsActive = data.IsActive;

                if (!string.IsNullOrWhiteSpace(_pickedImagePath) && File.Exists(_pickedImagePath))
                {
                    TryDeleteFile(row.ProfileImagePath);

                    row.ProfileImagePath = SaveProfilePictureToLocalFolder(row.UserId, _pickedImagePath);
                    _previewImagePath = row.ProfileImagePath;

                    LoadPreviewImage(_previewImagePath);
                    PicHintText.Text = "Saved";
                }

                db.SaveChanges();

                StatusText.Text = $"Updated user ID {row.UserId}.";
                RefreshUsers();
                SelectUserInList(row.UserId);

                LoadQuizStats(row.UserId);
                LoadWrongQuestionStats(row.UserId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Save failed:\n\n" + ex.Message,
                    "Save Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
            {
                MessageBox.Show("Select a user first.", "Delete User", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string mode = GetDeleteMode();

            var confirm = MessageBox.Show(
                $"Are you sure you want to {mode.ToUpper()} delete '{_selectedUser.FullName}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using var db = new PlanetContext();
                var row = db.Users.FirstOrDefault(x => x.UserId == _selectedUser.UserId);
                if (row == null)
                {
                    MessageBox.Show("User not found.", "Delete User", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (mode == "soft")
                {
                    row.IsActive = false;
                    db.SaveChanges();
                    StatusText.Text = $"Soft deleted user ID {row.UserId}.";
                }
                else
                {
                    var quizRows = db.QuizResults.Where(r => r.UserId == row.UserId).ToList();
                    db.QuizResults.RemoveRange(quizRows);

                    // If you have UserSessions, uncomment and ensure DbSet<UserSession> exists:
                    // var sessions = db.UserSessions.Where(s => s.UserId == row.UserId).ToList();
                    // db.UserSessions.RemoveRange(sessions);

                    db.Users.Remove(row);
                    db.SaveChanges();

                    TryDeleteFile(row.ProfileImagePath);

                    StatusText.Text = "Hard deleted user.";
                }

                ClearForm();
                RefreshUsers();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Delete failed:\n\n" + ex.Message +
                    "\n\nTip: Hard delete can fail if other tables reference this user.",
                    "Delete Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SelectUser_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUser == null)
            {
                MessageBox.Show("Select a user from the list first.", "Select User", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!_selectedUser.IsActive)
            {
                MessageBox.Show(
                    "This user is inactive (soft deleted). Enable Active and save, or choose another user.",
                    "Inactive User",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AppState.SetUser(_selectedUser.UserId);

            StatusText.Text = $"Selected user: {_selectedUser.FullName}";

            DialogResult = true;
            Close();
        }

        // ===================== VALIDATION =====================

        private ValidationResult ValidateForm(PlanetContext db)
        {
            try
            {
                var data = ReadFormValues(
                    NameBox.Text,
                    AgeBox.Text,
                    LocationBox.Text,
                    PhoneBox.Text,
                    EmailBox.Text,
                    JobBox.Text,
                    IsActiveCheck.IsChecked == true);

                ValidateBusinessRules(data, db);

                if (!string.IsNullOrWhiteSpace(_pickedImagePath))
                {
                    if (!ValidateImageFile(_pickedImagePath, out string imageError))
                        return ValidationResult.Fail(imageError);
                }

                return ValidationResult.Ok(data);
            }
            catch (Exception ex)
            {
                return ValidationResult.Fail(ex.Message);
            }
        }

        private void ValidateBusinessRules(
            (string FullName, int? Age, string? Location, string? Phone, string? Email, string? JobType, bool IsActive) data,
            PlanetContext db)
        {
            if (data.FullName.Length > MaxNameLength)
                throw new Exception($"Full Name cannot exceed {MaxNameLength} characters.");

            if (!string.IsNullOrWhiteSpace(data.Location) && data.Location.Length > MaxLocationLength)
                throw new Exception($"Location cannot exceed {MaxLocationLength} characters.");

            if (!string.IsNullOrWhiteSpace(data.Phone) && data.Phone.Length > MaxPhoneLength)
                throw new Exception($"Phone cannot exceed {MaxPhoneLength} characters.");

            if (!string.IsNullOrWhiteSpace(data.Email) && data.Email.Length > MaxEmailLength)
                throw new Exception($"Email cannot exceed {MaxEmailLength} characters.");

            if (!string.IsNullOrWhiteSpace(data.JobType) && data.JobType.Length > MaxJobLength)
                throw new Exception($"Job Type cannot exceed {MaxJobLength} characters.");

            if (!string.IsNullOrWhiteSpace(data.Phone) && !IsValidPhone(data.Phone))
                throw new Exception("Phone number is not valid. Use digits, spaces, +, -, or parentheses only.");

            if (!string.IsNullOrWhiteSpace(data.Email))
            {
                string normalizedEmail = data.Email.Trim().ToLowerInvariant();

                bool duplicateExists = db.Users.Any(u =>
                    u.Email != null &&
                    u.Email.ToLower() == normalizedEmail &&
                    (_selectedUser == null || u.UserId != _selectedUser.UserId));

                if (duplicateExists)
                    throw new Exception("Another user already exists with the same email address.");
            }
        }

        private static bool IsValidPhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return true;

            return Regex.IsMatch(phone, @"^[0-9+\-\s()]{7,25}$");
        }

        private static bool ValidateImageFile(string filePath, out string errorMessage)
        {
            errorMessage = "";

            if (string.IsNullOrWhiteSpace(filePath))
                return true;

            if (!File.Exists(filePath))
            {
                errorMessage = "Selected image file was not found.";
                return false;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string[] allowed = { ".jpg", ".jpeg", ".png", ".webp" };

            if (!allowed.Contains(ext))
            {
                errorMessage = "Only JPG, JPEG, PNG, and WEBP images are allowed.";
                return false;
            }

            try
            {
                var info = new FileInfo(filePath);
                const long maxSizeBytes = 5 * 1024 * 1024; // 5 MB

                if (info.Length > maxSizeBytes)
                {
                    errorMessage = "Image size must not exceed 5 MB.";
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(filePath);
                using var ms = new MemoryStream(bytes);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                return true;
            }
            catch
            {
                errorMessage = "The selected file is not a valid readable image.";
                return false;
            }
        }

        // ===================== HELPERS =====================

        private void LoadPreviewImage(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    ProfileImage.Source = null;
                    PicHintText.Text = "No picture selected";
                    return;
                }

                byte[] bytes = File.ReadAllBytes(path);

                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                }

                ProfileImage.Source = bmp;
            }
            catch
            {
                ProfileImage.Source = null;
                PicHintText.Text = "Invalid image";
            }
        }

        private string GetDeleteMode()
        {
            if (DeleteModeCombo.SelectedItem is ComboBoxItem item &&
                item.Tag?.ToString() is string tag &&
                (tag == "soft" || tag == "hard"))
            {
                return tag;
            }

            return "soft";
        }

        private void SelectUserInList(int userId)
        {
            if (UsersList.ItemsSource == null) return;

            foreach (var item in UsersList.Items)
            {
                if (item is User u && u.UserId == userId)
                {
                    UsersList.SelectedItem = item;
                    UsersList.ScrollIntoView(item);
                    break;
                }
            }
        }

        private static void TryDeleteFile(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Ignore file delete failures safely
            }
        }

        private static (
            string FullName,
            int? Age,
            string? Location,
            string? Phone,
            string? Email,
            string? JobType,
            bool IsActive)
        ReadFormValues(
            string name,
            string ageText,
            string location,
            string phone,
            string email,
            string job,
            bool isActive)
        {
            name = NormalizeWhitespace(name);
            location = NormalizeWhitespace(location);
            phone = NormalizeWhitespace(phone);
            email = NormalizeWhitespace(email);
            job = NormalizeWhitespace(job);

            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("Full Name is required.");

            if (name.Length < 2)
                throw new Exception("Full Name must be at least 2 characters.");

            if (!Regex.IsMatch(name, @"^[A-Za-z][A-Za-z\s.'\-]*$"))
                throw new Exception("Full Name contains invalid characters.");

            int? age = null;
            if (!string.IsNullOrWhiteSpace(ageText))
            {
                if (!int.TryParse(ageText.Trim(), out int a))
                    throw new Exception("Age must be a valid whole number.");

                if (a < 1 || a > 120)
                    throw new Exception("Age must be between 1 and 120.");

                age = a;
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                if (!IsValidEmail(email))
                    throw new Exception("Email is not valid.");

                email = email.ToLowerInvariant();
            }

            return (name, age, NullIfWhiteSpace(location), NullIfWhiteSpace(phone), NullIfWhiteSpace(email), NullIfWhiteSpace(job), isActive);
        }

        private (string FullName, int? Age, string? Location, string? Phone, string? Email, string? JobType, bool IsActive) ReadForm()
        {
            return ReadFormValues(
                NameBox.Text,
                AgeBox.Text,
                LocationBox.Text,
                PhoneBox.Text,
                EmailBox.Text,
                JobBox.Text,
                IsActiveCheck.IsChecked == true
            );
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return true;

            try
            {
                return Regex.IsMatch(
                    email,
                    @"^[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}$",
                    RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeWhitespace(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string SaveProfilePictureToLocalFolder(int userId, string sourcePath)
        {
            string appFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlanetExplorer",
                "ProfilePics");

            Directory.CreateDirectory(appFolder);

            string ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

            string fileName = $"Propic_{userId}_{DateTime.Now:yyyyMMddHHmmss}{ext}";
            string destPath = Path.Combine(appFolder, fileName);

            File.Copy(sourcePath, destPath, overwrite: true);

            return destPath;
        }

        // ===================== INTERNAL TYPES =====================

        private sealed class ValidationResult
        {
            public bool IsValid { get; private set; }
            public string ErrorMessage { get; private set; } = "";
            public (string FullName, int? Age, string? Location, string? Phone, string? Email, string? JobType, bool IsActive)? Data { get; private set; }

            public static ValidationResult Ok(
                (string FullName, int? Age, string? Location, string? Phone, string? Email, string? JobType, bool IsActive) data)
            {
                return new ValidationResult
                {
                    IsValid = true,
                    Data = data
                };
            }

            public static ValidationResult Fail(string error)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = error
                };
            }
        }
    }
}