using System.Linq;

namespace PlanetExplorer
{
    public static class DataMigration
    {
        public static void CopyPlanetsToSpaceItems()
        {
            using var db = new PlanetContext();

            var planets = db.Planets.ToList();

            foreach (var p in planets)
            {
                bool exists = db.SpaceItems
                                .Any(x => x.Name == p.Name && x.Type == "Planet");

                if (exists) continue;

                db.SpaceItems.Add(new SpaceItem
                {
                    Name = p.Name,
                    Type = "Planet",
                    ShortExplanation = p.Description,
                    TexturePath = p.TexturePath,
                    DiameterKm = p.DiameterKm,
                    MassKg = p.MassKg,
                    HasRings = p.HasRings,
                    RingTexturePath = p.RingTexturePath,

                    PositionXKm = null,
                    PositionYKm = null,
                    PositionZKm = null
                });
            }

            db.SaveChanges();
        }
    }
}
