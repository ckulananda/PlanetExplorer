using System.ComponentModel.DataAnnotations;

namespace PlanetExplorer
{
    public class SpaceItem
    {
        [Key]
        public int ItemId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        // Planet, Star, BlackHole, Pulsar, Quasar...
        [Required]
        public string Type { get; set; } = "Planet";

        public string? ShortExplanation { get; set; }
        public string? LongExplanation { get; set; }

        public string? TexturePath { get; set; }

        public double? RadiusKm { get; set; }
        public double? DiameterKm { get; set; }
        public double? MassKg { get; set; }

        // 🔵 Hybrid Measurement Fields

        // For planets (simple radial distance from sun)
        public double? DistanceFromSunKm { get; set; }

        // For deep space objects (full 3D coordinates)
        public double? PositionXKm { get; set; }
        public double? PositionYKm { get; set; }
        public double? PositionZKm { get; set; }

        public bool? HasRings { get; set; }
        public string? RingTexturePath { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
