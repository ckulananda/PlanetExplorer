using Microsoft.EntityFrameworkCore;

namespace PlanetExplorer
{
    public class PlanetContext : DbContext
    {
        public DbSet<Planet> Planets { get; set; }
        public DbSet<UserInteractionLog> UserInteractionLogs { get; set; }

        public DbSet<QuizResult> QuizResults { get; set; }

        public DbSet<SpaceItem> SpaceItems { get; set; }
        public DbSet<QuizQuestionEntity> QuizQuestions { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }

        public DbSet<QuizAnswerLog> QuizAnswerLogs { get; set; }






        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(
    "Server=LAPTOP-UKG1S7QQ\\SQLEXPRESS;Database=PlanetExplorerDB;Trusted_Connection=True;TrustServerCertificate=True;");

        }
    }
}
