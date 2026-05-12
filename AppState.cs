using System;
using System.Linq;

namespace PlanetExplorer
{
    public static class AppState
    {
        public static User? CurrentUser { get; private set; }
        public static bool IsLoggedIn => CurrentUser != null;

        public static event Action? UserChanged;

        // ✅ Always load user from DB so UserId is real
        public static void SetUser(int userId)
        {
            using var db = new PlanetContext();

            var user = db.Users.FirstOrDefault(u => u.UserId == userId && u.IsActive);
            if (user == null)
                throw new Exception("User not found or inactive.");

            CurrentUser = user;
            UserChanged?.Invoke();
        }

        public static void Logout()
        {
            CurrentUser = null;
            UserChanged?.Invoke();
        }
    }
}
