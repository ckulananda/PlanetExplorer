using System;
using System.IO;

namespace PlanetExplorer
{
    public static class ProfileImageStorage
    {
        // Folder: %AppData%\PlanetExplorer\Profiles
        public static string ProfilesFolder
        {
            get
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlanetExplorer",
                    "Profiles");

                Directory.CreateDirectory(root);
                return root;
            }
        }

        public static string SaveProfileImage(int userId, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Profile image source file not found.");

            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

            // ✅ Required naming rule
            var fileName = $"Propic_{userId}{ext}";
            var destPath = Path.Combine(ProfilesFolder, fileName);

            File.Copy(sourcePath, destPath, overwrite: true);
            return destPath;
        }

        public static void DeleteProfileImage(string? path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore: deleting file should not crash the app
            }
        }
    }
}
