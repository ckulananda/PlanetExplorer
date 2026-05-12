namespace PlanetExplorer
{
    public class Planet
    {
        public int PlanetId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public string? TexturePath { get; set; }   // make nullable-safe

        public double? DiameterKm { get; set; }
        public double? DistanceFromSunKm { get; set; }
        public double? MassKg { get; set; }
        public double? OrbitalPeriodDays { get; set; }

        public string? RingTexturePath { get; set; }

        // Optional if you later want to use them:
        public double? RadiusKm { get; set; }
        public double? RotationPeriodHours { get; set; }
        public double? AxialTiltDegrees { get; set; }
        public int? NumberOfMoons { get; set; }
        public bool? HasRings { get; set; }
    }
}
