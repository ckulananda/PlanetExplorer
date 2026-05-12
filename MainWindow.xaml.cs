using HelixToolkit.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static PlanetExplorer.QuizWindow;

namespace PlanetExplorer
{
    public partial class MainWindow : Window
    {
        // ===================== CAMERA CLIP PLANES =====================
        // Keeps rendering stable at very close and very far zoom levels.
        private const double CameraNear = 0.1;
        private const double CameraFar = 100000; // big = can zoom out far

        // ===================== TIMERS =====================
        // Orbit timer updates orbital translation in Solar view.
        private readonly System.Windows.Threading.DispatcherTimer _orbitTimer = new();

        // Spin timer rotates selected planet when in Planet view.
        private readonly System.Windows.Threading.DispatcherTimer _spinTimer = new();

        // Camera animation timer for smooth transitions.
        private readonly System.Windows.Threading.DispatcherTimer _camAnimTimer = new();

        // ===================== ORBIT CLOCK (STABLE dt) =====================
        // Stopwatch gives stable timing even if UI timer jitter occurs.
        private readonly Stopwatch _orbitClock = Stopwatch.StartNew();
        private double _lastOrbitSec = 0;

        // ===================== DATA =====================
        // Loaded from DB.
        private List<Planet> planets = new();

        // ===================== SCENE TRACKING =====================
        // Tracks visuals added to the viewport so we can remove/rebuild safely.
        private readonly List<Visual3D> _sceneVisuals = new();
        private SphereVisual3D? _starSphere;
        private SphereVisual3D? _sunSphere;

        // Prevents re-entrancy while rebuilding the scene.
        private bool _isBuildingScene = false;

        // ===================== PLANET NODES (STABLE ORBIT) =====================
        // Holds references to each planet's root + orbit translate + spin transform.
        private sealed class PlanetNode
        {
            public required int PlanetId;
            public required ModelVisual3D Root;
            public required TranslateTransform3D OrbitTranslate;

            public required SphereVisual3D Sphere;
            public required AxisAngleRotation3D SpinAxis;
            public required RotateTransform3D SpinRotate;
        }

        private readonly Dictionary<int, PlanetNode> _planetNodes = new();

        // ===================== HIT MAPPING (CLICK/HOVER) =====================
        // Allows mapping click/hover hit results back to PlanetId.
        private readonly Dictionary<Visual3D, int> _visualToPlanetId = new();
        private readonly Dictionary<Model3D, int> _modelToPlanetId = new();

        // ===================== ORBIT STATE =====================
        // Orbit angles evolve with omega and dt.
        private readonly Dictionary<int, double> _orbitAngles = new(); // radians
        private readonly Dictionary<int, double> _orbitOmega = new();  // rad/sec
        private readonly Dictionary<int, double> _orbitRadii = new();  // scene units

        // ===================== ORBIT RINGS =====================
        private readonly Dictionary<int, LinesVisual3D> _orbitRings = new();
        private bool _showOrbitRings = true;

        // ===================== SELECTION / MODE =====================
        private Planet? _currentPlanet;
        private string _viewMode = "Solar"; // Solar / Planet

        // Hover tooltip state.
        private int? _hoverPlanetId = null;

        // Selected label state.
        private BillboardTextVisual3D? _selectedPlanetLabel;

        // ===================== CAMERA ANIMATION =====================
        private CameraAnimationState? _camAnim;
        private bool _isCameraAnimating = false;

        private const int CameraAnimMs = 520;
        private const int CameraAnimTickMs = 16;

        // ===================== LOGGER =====================
        private readonly InteractionLogBuffer _logger = new(flushEverySeconds: 5);
        private readonly Stopwatch _planetStopwatch = new();
        private int? _activePlanetIdForTiming = null;

        // ===================== MEASURE TOOL =====================
        private bool _measureMode = false;
        private SpaceItem? _measureA;
        private SpaceItem? _measureB;
        private LinesVisual3D? _measureLine;

        // Cached DB items for measurement lookup by name.
        private Dictionary<string, SpaceItem> _spaceItemByName = new(StringComparer.OrdinalIgnoreCase);

        private enum DistanceMethod { ThreeD, RadialFromSun }

        private sealed class DistanceResult
        {
            public bool Success { get; init; }
            public double DistanceKm { get; init; }
            public DistanceMethod UsedMethod { get; init; }
            public string? ErrorMessage { get; init; }
        }

        // ===================== VISUAL SCALE DATA =====================
        // Scales planets relative to Earth radius (visual-only).
        private readonly Dictionary<string, double> _radiusRatio = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Mercury", 0.383 },
            { "Venus",   0.949 },
            { "Earth",   1.000 },
            { "Mars",    0.532 },
            { "Jupiter", 11.209 },
            { "Saturn",  9.449 },
            { "Uranus",  4.007 },
            { "Neptune", 3.883 }
        };

        private const double BasePlanetRadius = 0.40;
        private const double MaxPlanetRadius = 3.2;

        // Scene orbit scaling factors.
        private const double DistanceScale = 0.000000006; // 6e-9
        private const double MinOrbitGap = 3.0;
        private const double SunSafetyRadius = 6.0;

        // Orbit speed clamp (rad/sec) for pleasant animation.
        private const double MinOmega = 0.20;
        private const double MaxOmega = 1.20;

        private const double StarSphereRadius = 500;
        private const double MaxAllowedOrbitRadius = StarSphereRadius * 0.85;

        // Material cache to reduce GPU churn and repeated Bitmap decoding.
        private readonly Dictionary<string, Material> _materialCache = new(StringComparer.OrdinalIgnoreCase);

        // ===================== CONSTRUCTOR =====================
        // Initializes timers, hooks input events, loads DB data, and builds the 3D scene.
        public MainWindow()
        {
            InitializeComponent();
            if (HelixView == null) return;

            // Fix zoom out limits (near/far clip planes).
            if (HelixView.Camera is ProjectionCamera pc)
            {
                pc.NearPlaneDistance = CameraNear;
                pc.FarPlaneDistance = CameraFar;
            }

            // Apply access rules (logged-in gating for quiz/measure).
            AppState.UserChanged += ApplyAccessRules;
            ApplyAccessRules();

            // Helix input events for selection + hover tooltip.
            HelixView.MouseDown += HelixView_MouseDown;
            HelixView.MouseMove += HelixView_MouseMove;
            HelixView.MouseLeave += (_, __) => ClearHoverTip();

            // Measure mode toggle wiring.
            if (MeasureToggle != null)
            {
                MeasureToggle.Checked += (_, __) =>
                {
                    _measureMode = true;
                    ClearMeasurement();
                    if (MeasureResultText != null)
                        MeasureResultText.Text = "Click first object...";
                };

                MeasureToggle.Unchecked += (_, __) =>
                {
                    _measureMode = false;
                    ClearMeasurement();
                    if (MeasureResultText != null)
                        MeasureResultText.Text = "";
                };
            }

            // Orbit rings toggle default.
            if (OrbitRingsToggle != null)
                _showOrbitRings = OrbitRingsToggle.IsChecked == true;

            // Orbit animation timer.
            _orbitTimer.Interval = TimeSpan.FromMilliseconds(16);
            _orbitTimer.Tick += (_, __) => AnimateOrbitsStable();
            _orbitTimer.Start();

            // Spin animation timer.
            _spinTimer.Interval = TimeSpan.FromMilliseconds(30);
            _spinTimer.Tick += (_, __) => SpinSelectedPlanetStable();
            _spinTimer.Start();

            // Camera animation timer.
            _camAnimTimer.Interval = TimeSpan.FromMilliseconds(CameraAnimTickMs);
            _camAnimTimer.Tick += CameraAnimTick;

            // Load DB data and build 3D scene.
            LoadPlanetsAndSpaceItems();
            Build3DScene();

            // Cleanup on window close.
            Closed += (_, __) =>
            {
                _orbitTimer.Stop();
                _spinTimer.Stop();
                _camAnimTimer.Stop();
                _logger.Dispose();
            };
        }

        // ===================== CAMERA RESET =====================
        // Resets camera to a stable, wide solar-system view.
        private void ResetSolarCamera()
        {
            if (HelixView?.Camera is not PerspectiveCamera cam) return;

            cam.Position = new Point3D(0, 35, 170);
            cam.LookDirection = new Vector3D(0, -12, -170);
            cam.UpDirection = new Vector3D(0, 1, 0);
            cam.FieldOfView = 45;

            cam.NearPlaneDistance = CameraNear;
            cam.FarPlaneDistance = CameraFar;
        }

        // ===================== ACCESS RULES =====================
        // Enables/disables UI features based on login state.
        private void ApplyAccessRules()
        {
            bool ok = AppState.IsLoggedIn;

            if (UserStatusText != null)
            {
                UserStatusText.Text = ok
                    ? $"User: {AppState.CurrentUser!.FullName}"
                    : "No user selected. Register to unlock Quiz/Measure.";
            }

            if (TakeQuizButton != null) TakeQuizButton.IsEnabled = ok;
            if (MeasureToggle != null) MeasureToggle.IsEnabled = ok;
            if (SpeedCombo != null) SpeedCombo.IsEnabled = ok;

            if (!ok && MeasureResultText != null)
                MeasureResultText.Text = "";
        }

        // ===================== PROFILE =====================
        // Opens the profile window (registration/login UI).
        private void OpenProfile_Click(object sender, RoutedEventArgs e)
        {
            var w = new ProfileWindow { Owner = this };
            w.ShowDialog();
        }

        // ===================== DATA LOAD =====================
        // Loads planets and SpaceItems from DB, populates list UI and caches measurement items.
        private void LoadPlanetsAndSpaceItems()
        {
            try
            {
                using (var db = new PlanetContext())
                {
                    planets = db.Planets
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                        .ToList();

                    _spaceItemByName = db.SpaceItems
                        .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.Name))
                        .ToList()
                        .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                }

                if (PlanetList != null)
                {
                    PlanetList.ItemsSource = planets;
                    PlanetList.DisplayMemberPath = "Name";

                    PlanetList.SelectionChanged -= PlanetList_SelectionChanged;
                    PlanetList.SelectionChanged += PlanetList_SelectionChanged;

                    if (planets.Count > 0)
                        PlanetList.SelectedIndex = 0;
                }

                if (AvgScoreText != null)
                    AvgScoreText.Text = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load planets.\n\n{ex}");
            }
        }

        // ===================== SCENE BUILD =====================
        // Rebuilds the entire 3D scene: stars, sun, planets, rings, mappings.
        private void Build3DScene()
        {
            if (HelixView == null) return;
            if (_isBuildingScene) return;

            try
            {
                _isBuildingScene = true;

                RemoveSceneVisuals();

                AddStarBackground();
                BuildSun();

                _planetNodes.Clear();
                _visualToPlanetId.Clear();
                _modelToPlanetId.Clear();
                _orbitRings.Clear();
                _orbitRadii.Clear();
                _orbitOmega.Clear();

                ComputeOrbitRadiiAndSpeedsStable();

                if (_showOrbitRings)
                    BuildOrbitRings();

                foreach (var p in planets)
                {
                    double radius = GetVisualRadius(p);
                    double orbitR = _orbitRadii.TryGetValue(p.PlanetId, out var r) ? r : 12.0;

                    // Seed stable initial angles (keeps them spread out).
                    if (!_orbitAngles.ContainsKey(p.PlanetId))
                        _orbitAngles[p.PlanetId] = p.PlanetId * 0.7;

                    double a = _orbitAngles[p.PlanetId];

                    // Root container is moved by orbit translation.
                    var root = new ModelVisual3D();

                    var orbitTranslate = new TranslateTransform3D(
                        orbitR * Math.Cos(a), 0, orbitR * Math.Sin(a)
                    );
                    root.Transform = orbitTranslate;

                    // Planet sphere stays at local origin and spins locally.
                    var spinAxis = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
                    var spinRotate = new RotateTransform3D(spinAxis, new Point3D(0, 0, 0));

                    var mat = MakeMaterialSafe(p.TexturePath, fallbackColor: Colors.Gray);

                    var sphere = new SphereVisual3D
                    {
                        Radius = radius,
                        Center = new Point3D(0, 0, 0),
                        ThetaDiv = 64,
                        PhiDiv = 64,
                        Material = mat,
                        Transform = spinRotate
                    };

                    root.Children.Add(sphere);
                    AddToScene(root);

                    var node = new PlanetNode
                    {
                        PlanetId = p.PlanetId,
                        Root = root,
                        OrbitTranslate = orbitTranslate,
                        Sphere = sphere,
                        SpinAxis = spinAxis,
                        SpinRotate = spinRotate
                    };

                    _planetNodes[p.PlanetId] = node;

                    // Map visuals/models to planetId for click/hover.
                    _visualToPlanetId[sphere] = p.PlanetId;
                    _visualToPlanetId[root] = p.PlanetId;

                    if (sphere.Model != null)
                        _modelToPlanetId[sphere.Model] = p.PlanetId;
                }

                // Reset camera to stable solar framing (avoids zoom traps).
                ResetSolarCamera();

                // Reset orbit clock baseline after rebuilding.
                _lastOrbitSec = _orbitClock.Elapsed.TotalSeconds;
            }
            finally
            {
                _isBuildingScene = false;
            }
        }

        // Opens CosmicWindow (duplicate handlers kept but cleaned).
        private void CosmicView_Click(object sender, RoutedEventArgs e)
        {
            var w = new CosmicWindow { Owner = this };
            w.Show();
        }

        // Opens CosmicWindow (map button).
        private void CosmicMap_Click(object sender, RoutedEventArgs e)
        {
            var w = new CosmicWindow { Owner = this };
            w.Show();
        }

        // Removes all visuals that were previously added to the scene.
        private void RemoveSceneVisuals()
        {
            if (HelixView == null) return;

            foreach (var v in _sceneVisuals.ToList())
            {
                if (v != null && HelixView.Children.Contains(v))
                    HelixView.Children.Remove(v);
            }
            _sceneVisuals.Clear();

            _starSphere = null;
            _sunSphere = null;

            _orbitRings.Clear();
            _planetNodes.Clear();
            _visualToPlanetId.Clear();
            _modelToPlanetId.Clear();

            ClearMeasurement();
            RemoveSelectedPlanetLabel();
            ClearHoverTip();
        }

        // Adds a Visual3D to the viewport and tracks it for cleanup.
        private void AddToScene(Visual3D visual)
        {
            if (HelixView == null) return;
            HelixView.Children.Add(visual);
            _sceneVisuals.Add(visual);
        }

        // Adds a star-texture sphere as background skydome.
        private void AddStarBackground()
        {
            if (HelixView == null) return;

            var starMat = MakeMaterialSafe("Assets/Textures/stars.jpg", Colors.Black);

            _starSphere = new SphereVisual3D
            {
                Radius = StarSphereRadius,
                Center = new Point3D(0, 0, 0),
                ThetaDiv = 64,
                PhiDiv = 64,
                Material = starMat,
                BackMaterial = starMat
            };

            HelixView.Children.Insert(0, _starSphere);
            _sceneVisuals.Add(_starSphere);
        }

        // Builds sun core + translucent glow shell.
        private void BuildSun()
        {
            _sunSphere = new SphereVisual3D
            {
                Radius = 2.5,
                Center = new Point3D(0, 0, 0),
                ThetaDiv = 64,
                PhiDiv = 64,
                Material = MakeMaterialSafe("Assets/Textures/sun.jpg", Colors.Orange)
            };
            AddToScene(_sunSphere);

            // Glow shell for visual effect.
            var glow = new SphereVisual3D
            {
                Radius = 3.7,
                Center = new Point3D(0, 0, 0),
                ThetaDiv = 64,
                PhiDiv = 64,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(55, 255, 190, 80))),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(55, 255, 190, 80)))
            };
            AddToScene(glow);
        }

        // ===================== ORBIT RADII + SPEED =====================
        // Computes orbit radii (with spacing constraints) and omega (speed) per planet.
        private void ComputeOrbitRadiiAndSpeedsStable()
        {
            // Sort: valid distances first, then by distance, then id.
            var sorted = planets
                .OrderBy(p => p.DistanceFromSunKm.HasValue && p.DistanceFromSunKm.Value > 0 ? 0 : 1)
                .ThenBy(p => p.DistanceFromSunKm ?? double.MaxValue)
                .ThenBy(p => p.PlanetId)
                .ToList();

            double currentOrbit = SunSafetyRadius;

            foreach (var p in sorted)
            {
                double visualRadius = GetVisualRadius(p);

                double raw = 0;
                if (p.DistanceFromSunKm.HasValue && p.DistanceFromSunKm.Value > 0)
                    raw = p.DistanceFromSunKm.Value * DistanceScale;

                // Ensure minimum orbit spacing based on planet size to avoid overlaps.
                double minBySize = visualRadius * 7;

                // Choose the best orbit radius within constraints.
                double chosen = Math.Max(raw > 0 ? raw : 0, Math.Max(currentOrbit, minBySize));
                chosen = Math.Min(chosen, MaxAllowedOrbitRadius);

                _orbitRadii[p.PlanetId] = chosen;

                currentOrbit = chosen + (visualRadius * 6) + MinOrbitGap;
                currentOrbit = Math.Min(currentOrbit, MaxAllowedOrbitRadius);
            }

            // Compute omega (angular velocity) per planet.
            foreach (var p in planets)
            {
                double orbitR = _orbitRadii.TryGetValue(p.PlanetId, out var r) ? r : 12.0;

                double omega;
                if (p.OrbitalPeriodDays.HasValue && p.OrbitalPeriodDays.Value > 0)
                {
                    // Visual period scaling: smaller number = faster orbit animation.
                    double periodSecVisual = p.OrbitalPeriodDays.Value * 1.2;
                    omega = (Math.PI * 2.0) / Math.Max(1.0, periodSecVisual);
                }
                else
                {
                    // Fallback speed if orbital period isn't provided.
                    omega = 0.9 * (SunSafetyRadius / Math.Max(SunSafetyRadius, orbitR));
                }

                _orbitOmega[p.PlanetId] = Clamp(omega, MinOmega, MaxOmega);
            }
        }

        // ===================== ORBIT ANIMATION =====================
        // Updates planet orbit positions each tick (Solar view only).
        private void AnimateOrbitsStable()
        {
            if (_isBuildingScene) return;
            if (_viewMode == "Planet") return;

            double now = _orbitClock.Elapsed.TotalSeconds;
            double dt = now - _lastOrbitSec;
            _lastOrbitSec = now;

            if (dt <= 0) return;

            // Clamp dt to avoid big jumps after pauses.
            if (dt > 0.1) dt = 0.1;

            foreach (var p in planets)
            {
                if (!_planetNodes.TryGetValue(p.PlanetId, out var node))
                    continue;

                double orbitR = _orbitRadii.TryGetValue(p.PlanetId, out var r) ? r : 12.0;
                double omega = _orbitOmega.TryGetValue(p.PlanetId, out var w) ? w : 0.5;

                double a = _orbitAngles.TryGetValue(p.PlanetId, out var ang) ? ang : 0;
                a += omega * dt;
                if (a > Math.PI * 2) a -= Math.PI * 2;
                _orbitAngles[p.PlanetId] = a;

                node.OrbitTranslate.OffsetX = orbitR * Math.Cos(a);
                node.OrbitTranslate.OffsetY = 0;
                node.OrbitTranslate.OffsetZ = orbitR * Math.Sin(a);

                // Keep label tracking the selected planet while orbiting.
                if (_selectedPlanetLabel != null && _currentPlanet != null && _currentPlanet.PlanetId == p.PlanetId)
                {
                    _selectedPlanetLabel.Position = new Point3D(
                        node.OrbitTranslate.OffsetX,
                        node.Sphere.Radius * 1.6,
                        node.OrbitTranslate.OffsetZ
                    );
                }
            }
        }

        // ===================== SPIN SELECTED =====================
        // Spins the currently selected planet (Planet view).
        private void SpinSelectedPlanetStable()
        {
            if (_isBuildingScene) return;
            if (_currentPlanet == null) return;
            if (!_planetNodes.TryGetValue(_currentPlanet.PlanetId, out var node)) return;

            node.SpinAxis.Angle = (node.SpinAxis.Angle + 1) % 360;
        }

        // ===================== ORBIT RINGS =====================
        // Builds orbit ring polylines for each planet.
        private void BuildOrbitRings()
        {
            foreach (var p in planets)
            {
                if (!_orbitRadii.TryGetValue(p.PlanetId, out var radius))
                    continue;

                var ring = new LinesVisual3D
                {
                    Thickness = 1.0,
                    Color = Color.FromArgb(90, 255, 255, 255),
                    Points = BuildOrbitCirclePoints(radius, segments: 256)
                };

                AddToScene(ring);
                _orbitRings[p.PlanetId] = ring;
            }
        }

        // Builds points around a circle on the XZ plane.
        private static Point3DCollection BuildOrbitCirclePoints(double radius, int segments)
        {
            var pts = new Point3DCollection();
            for (int i = 0; i <= segments; i++)
            {
                double t = (Math.PI * 2.0) * i / segments;
                pts.Add(new Point3D(radius * Math.Cos(t), 0, radius * Math.Sin(t)));
            }
            return pts;
        }

        // Shows/hides orbit rings by rebuilding them.
        private void SetOrbitRingsVisible(bool visible)
        {
            _showOrbitRings = visible;

            foreach (var ring in _orbitRings.Values.ToList())
            {
                if (HelixView != null && HelixView.Children.Contains(ring))
                    HelixView.Children.Remove(ring);

                _sceneVisuals.Remove(ring);
            }
            _orbitRings.Clear();

            if (visible)
                BuildOrbitRings();
        }

        // ===================== INPUT / HITS =====================
        // Handles click selection of planets via Helix hit testing.
        private void HelixView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (HelixView == null) return;
            if (_isBuildingScene) return;

            var pos = e.GetPosition(HelixView);
            var hits = HelixView.Viewport.FindHits(pos);
            if (hits == null || hits.Count == 0) return;

            var hit = hits[0];

            // Visual hit path.
            if (hit.Visual != null && _visualToPlanetId.TryGetValue(hit.Visual, out int planetId))
            {
                SelectPlanetById(planetId, e);
                return;
            }

            // Model hit path.
            if (hit.Model != null && _modelToPlanetId.TryGetValue(hit.Model, out planetId))
            {
                SelectPlanetById(planetId, e);
                return;
            }
        }

        // Selects planet either for measurement (if enabled) or for UI selection.
        private void SelectPlanetById(int planetId, MouseButtonEventArgs e)
        {
            var planet = planets.FirstOrDefault(x => x.PlanetId == planetId);
            if (planet == null) return;

            if (_measureMode)
            {
                HandleMeasurementSelection(planet.Name);
                e.Handled = true;
                return;
            }

            if (PlanetList != null)
                PlanetList.SelectedItem = planet;

            e.Handled = true;
        }

        // ===================== HOVER TOOLTIP =====================
        // Updates hover tooltip based on Helix hit testing.
        private void HelixView_MouseMove(object sender, MouseEventArgs e)
        {
            if (HelixView == null) return;
            if (_isBuildingScene) return;

            var pos = e.GetPosition(HelixView);
            var hits = HelixView.Viewport.FindHits(pos);

            if (hits == null || hits.Count == 0)
            {
                ClearHoverTip();
                return;
            }

            var hit = hits[0];

            if (hit.Visual != null && _visualToPlanetId.TryGetValue(hit.Visual, out int planetId))
            {
                if (_hoverPlanetId != planetId)
                {
                    _hoverPlanetId = planetId;
                    ShowHoverTip(planetId);
                }
                return;
            }

            if (hit.Model != null && _modelToPlanetId.TryGetValue(hit.Model, out planetId))
            {
                if (_hoverPlanetId != planetId)
                {
                    _hoverPlanetId = planetId;
                    ShowHoverTip(planetId);
                }
                return;
            }

            ClearHoverTip();
        }

        // Shows the hover tooltip panel for a planet.
        private void ShowHoverTip(int planetId)
        {
            var p = planets.FirstOrDefault(x => x.PlanetId == planetId);
            if (p == null) { ClearHoverTip(); return; }

            if (HoverTipTitle != null) HoverTipTitle.Text = p.Name;

            string body =
                (p.DiameterKm.HasValue ? $"Diameter: {p.DiameterKm.Value:N0} km\n" : "") +
                (p.DistanceFromSunKm.HasValue ? $"Distance from Sun: {p.DistanceFromSunKm.Value:N0} km\n" : "") +
                (p.OrbitalPeriodDays.HasValue ? $"Orbital period: {p.OrbitalPeriodDays.Value:N0} days\n" : "");

            if (string.IsNullOrWhiteSpace(body))
                body = "Click to select.";

            if (HoverTipBody != null) HoverTipBody.Text = body.Trim();
            if (HoverTip != null) HoverTip.Visibility = Visibility.Visible;
        }

        // Hides hover tooltip and clears text.
        private void ClearHoverTip()
        {
            _hoverPlanetId = null;
            if (HoverTip != null) HoverTip.Visibility = Visibility.Collapsed;
            if (HoverTipTitle != null) HoverTipTitle.Text = "";
            if (HoverTipBody != null) HoverTipBody.Text = "";
        }

        // ===================== LIST SELECTION =====================
        // Called when user changes planet selection in the ListBox.
        private void PlanetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isBuildingScene) return;
            if (PlanetList?.SelectedItem is not Planet selectedPlanet) return;

            _currentPlanet = selectedPlanet;

            if (PlanetName != null) PlanetName.Text = selectedPlanet.Name;
            if (PlanetDescription != null) PlanetDescription.Text = selectedPlanet.Description ?? "";

            _viewMode = "Planet";

            StartPlanetTiming(_currentPlanet.PlanetId);
            ShowSelectedPlanetLabel(_currentPlanet.PlanetId);
            FocusCameraOnPlanetSmooth(_currentPlanet.PlanetId);
        }

        // ===================== LABEL =====================
        // Displays a billboard label above the selected planet.
        private void ShowSelectedPlanetLabel(int planetId)
        {
            var p = planets.FirstOrDefault(x => x.PlanetId == planetId);
            if (p == null) return;

            if (!_planetNodes.TryGetValue(planetId, out var node))
                return;

            RemoveSelectedPlanetLabel();

            _selectedPlanetLabel = new BillboardTextVisual3D
            {
                Text = p.Name,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
                FontSize = 18,
                Position = new Point3D(
                    node.OrbitTranslate.OffsetX,
                    node.Sphere.Radius * 1.6,
                    node.OrbitTranslate.OffsetZ
                )
            };

            AddToScene(_selectedPlanetLabel);
        }

        // Removes the selected label from scene if present.
        private void RemoveSelectedPlanetLabel()
        {
            if (_selectedPlanetLabel != null && HelixView != null)
            {
                if (HelixView.Children.Contains(_selectedPlanetLabel))
                    HelixView.Children.Remove(_selectedPlanetLabel);

                _sceneVisuals.Remove(_selectedPlanetLabel);
                _selectedPlanetLabel = null;
            }
        }

        // ===================== BUTTONS =====================
        // Switches back to Solar mode and resets camera.
        private void SolarView_Click(object sender, RoutedEventArgs e)
        {
            _camAnimTimer.Stop();
            _camAnim = null;
            _isCameraAnimating = false;

            RemoveSelectedPlanetLabel();
            ClearHoverTip();

            _viewMode = "Solar";
            EndPlanetTimingIfRunning(reason: "BackToSolar");

            _lastOrbitSec = _orbitClock.Elapsed.TotalSeconds;

            ResetSolarCamera();
        }

        // Focuses camera on currently selected planet (stays in Planet mode).
        private void PlanetView_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlanet == null) return;

            _viewMode = "Planet";
            ShowSelectedPlanetLabel(_currentPlanet.PlanetId);
            FocusCameraOnPlanetSmooth(_currentPlanet.PlanetId);
        }

        // Toggle orbit rings on.
        private void OrbitRingsToggle_Checked(object sender, RoutedEventArgs e) => SetOrbitRingsVisible(true);

        // Toggle orbit rings off.
        private void OrbitRingsToggle_Unchecked(object sender, RoutedEventArgs e) => SetOrbitRingsVisible(false);

        // ===================== CAMERA FOCUS =====================
        // Smoothly animates camera to look at the selected planet at a reasonable distance.
        private void FocusCameraOnPlanetSmooth(int planetId)
        {
            if (HelixView?.Camera is not PerspectiveCamera cam) return;
            if (!_planetNodes.TryGetValue(planetId, out var node)) return;

            var center = new Point3D(node.OrbitTranslate.OffsetX, 0, node.OrbitTranslate.OffsetZ);

            var dir = cam.LookDirection;
            if (dir.Length == 0) dir = new Vector3D(0, 0, -1);
            dir.Normalize();

            double distance = Math.Max(8, node.Sphere.Radius * 6.0);

            var targetPos = center - dir * distance;
            var targetLook = center - targetPos;

            StartCameraAnimation(
                from: CameraSnapshot.From(cam),
                to: new CameraSnapshot
                {
                    Position = targetPos,
                    LookDirection = targetLook,
                    UpDirection = new Vector3D(0, 1, 0),
                    FieldOfView = cam.FieldOfView
                }
            );
        }

        // Starts an eased camera animation between two snapshots.
        private void StartCameraAnimation(CameraSnapshot from, CameraSnapshot to)
        {
            if (HelixView?.Camera is not PerspectiveCamera cam) return;

            _camAnimTimer.Stop();

            _camAnim = new CameraAnimationState
            {
                Camera = cam,
                From = from,
                To = to,
                StartUtc = DateTime.UtcNow,
                DurationMs = CameraAnimMs
            };

            _isCameraAnimating = true;
            _camAnimTimer.Start();
        }

        // Timer tick handler: interpolates camera state until animation completes.
        private void CameraAnimTick(object? sender, EventArgs e)
        {
            if (_isBuildingScene)
            {
                _camAnimTimer.Stop();
                _camAnim = null;
                _isCameraAnimating = false;
                return;
            }

            if (_camAnim == null)
            {
                _camAnimTimer.Stop();
                _isCameraAnimating = false;
                return;
            }

            var cam = _camAnim.Camera;

            var elapsed = (DateTime.UtcNow - _camAnim.StartUtc).TotalMilliseconds;
            double t = elapsed / _camAnim.DurationMs;

            if (t >= 1.0)
            {
                _camAnim.To.ApplyTo(cam);
                _camAnim = null;
                _camAnimTimer.Stop();
                _isCameraAnimating = false;
                return;
            }

            double te = EaseOutCubic(t);

            cam.Position = Lerp(_camAnim.From.Position, _camAnim.To.Position, te);
            cam.LookDirection = Lerp(_camAnim.From.LookDirection, _camAnim.To.LookDirection, te);
            cam.UpDirection = Lerp(_camAnim.From.UpDirection, _camAnim.To.UpDirection, te);
            cam.FieldOfView = Lerp(_camAnim.From.FieldOfView, _camAnim.To.FieldOfView, te);

            // Keep planes stable during animation too.
            cam.NearPlaneDistance = CameraNear;
            cam.FarPlaneDistance = CameraFar;
        }

        // Easing function (fast start, slow end).
        private static double EaseOutCubic(double t)
        {
            double u = 1.0 - t;
            return 1.0 - (u * u * u);
        }

        // Linear interpolation helpers.
        private static Point3D Lerp(Point3D a, Point3D b, double t)
            => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        private static Vector3D Lerp(Vector3D a, Vector3D b, double t)
            => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        private static double Lerp(double a, double b, double t)
            => a + (b - a) * t;

        // ===================== QUIZ LOGGING =====================
        // Saves per-question answer logs from the quiz window into DB.
        private void SaveQuizAnswerLogs(int? planetId, int? itemId, List<QuizAttempt> attempts)
        {

            int count = attempts?.Count ?? 0;

            // Debug popup BEFORE early return so you can see if it's null/empty.
            MessageBox.Show(
                $"Attempts count = {count}\n" +
                $"LoggedIn = {AppState.IsLoggedIn}\n" +
                $"PlanetId = {(planetId.HasValue ? planetId.Value.ToString() : "null")}\n" +
                $"ItemId = {(itemId.HasValue ? itemId.Value.ToString() : "null")}"
            );

            if (!AppState.IsLoggedIn || attempts == null || attempts.Count == 0)
                return;

            try
            {
                using var db = new PlanetContext();

                var rows = attempts.Select(a => new QuizAnswerLog
                {
                    UserId = AppState.CurrentUser!.UserId,
                    PlanetId = planetId,
                    ItemId = itemId,
                    QuizQuestionEntityId = a.QuestionId,
                    SelectedIndex = a.SelectedIndex,
                    IsCorrect = a.IsCorrect,
                    Timestamp = DateTime.Now,
                    SessionId = _logger.SessionId
                }).ToList();

                db.QuizAnswerLogs.AddRange(rows);

                int saved = db.SaveChanges(); // EF returns how many state entries were written

                // Debug popup AFTER save
                var first = attempts[0];
                MessageBox.Show(
                    $"Saved QuizAnswerLogs.\n" +
                    $"EF SaveChanges() returned: {saved}\n" +
                    $"First attempt => QID={first.QuestionId}, Selected={first.SelectedIndex}, Correct={first.IsCorrect}"
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save quiz answer history:\n\n" + ex);
            }
            // (Optional debug) Remove if not needed.
            // MessageBox.Show("Attempts count = " + (attempts?.Count ?? 0));

            if (!AppState.IsLoggedIn || attempts == null || attempts.Count == 0)
                return;

            try
            {
                using var db = new PlanetContext();

                var rows = attempts.Select(a => new QuizAnswerLog
                {
                    UserId = AppState.CurrentUser!.UserId,
                    PlanetId = planetId,
                    ItemId = itemId,
                    QuizQuestionEntityId = a.QuestionId,
                    SelectedIndex = a.SelectedIndex,
                    IsCorrect = a.IsCorrect,
                    Timestamp = DateTime.Now,
                    SessionId = _logger.SessionId
                }).ToList();

                db.QuizAnswerLogs.AddRange(rows);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save quiz answer history:\n\n" + ex.Message);
            }
        }

        // ===================== QUIZ =====================
        // Converts DB entity rows to the in-memory quiz question model.
        private static List<QuizQuestion> ToQuizQuestions(List<QuizQuestionEntity> rows)
        {
            return rows.Select(r => new QuizQuestion
            {
                QuestionId = r.QuestionId,
                QuestionText = r.QuestionText,
                Options = new List<string> { r.OptionA, r.OptionB, r.OptionC, r.OptionD },
                CorrectIndex = r.CorrectIndex
            }).ToList();
        }

        // Launches either a planet quiz (if planet selected) or a solar system quiz.
        private void TakeQuizButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AppState.IsLoggedIn)
            {
                MessageBox.Show("Please register/login first to take quizzes.");
                return;
            }

            using var db = new PlanetContext();

            // Planet-specific quiz flow.
            if (_currentPlanet != null)
            {
                var item = db.SpaceItems.FirstOrDefault(x => x.Name == _currentPlanet.Name && x.IsActive);
                if (item == null)
                {
                    MessageBox.Show("This planet is not found in SpaceItems.");
                    return;
                }

                var rows = db.QuizQuestions
                    .Where(q => q.IsActive && q.ItemId == item.ItemId)
                    .OrderBy(_ => Guid.NewGuid())
                    .Take(7)
                    .ToList();

                // If not enough item-specific questions, fill with generic planet topic questions.
                if (rows.Count < 7)
                {
                    var extra = db.QuizQuestions
                        .Where(q => q.IsActive && q.ItemId == null && q.TopicType == "Planet")
                        .OrderBy(_ => Guid.NewGuid())
                        .Take(7 - rows.Count)
                        .ToList();

                    rows.AddRange(extra);
                }

                if (rows.Count == 0)
                {
                    MessageBox.Show("No quiz questions found in DB for this planet/topic.");
                    return;
                }

                var questions = ToQuizQuestions(rows);
                var qw = new QuizWindow(item.ItemId, item.Name, questions) { Owner = this };
                var ok = qw.ShowDialog();

                if (ok == true)
                {
                    SaveQuizResult(_currentPlanet.PlanetId, item.ItemId, qw.FinalScore, qw.TotalQuestions);

                    SaveQuizAnswerLogs(
                        planetId: _currentPlanet.PlanetId,
                        itemId: item.ItemId,
                        attempts: qw.Attempts
                    );

                    MessageBox.Show($"Score: {qw.FinalScore} / {qw.TotalQuestions}");
                }

                return;
            }

            // Solar system quiz flow.
            var solarRows = db.QuizQuestions
                .Where(q => q.IsActive && q.ItemId == null && q.TopicType == "SolarSystem")
                .OrderBy(_ => Guid.NewGuid())
                .Take(10)
                .ToList();

            if (solarRows.Count == 0)
            {
                MessageBox.Show("No SolarSystem quiz questions found in DB.");
                return;
            }

            var solarQuestions = ToQuizQuestions(solarRows);
            var solarQuiz = new QuizWindow(0, "Solar System Quiz", solarQuestions) { Owner = this };
            var okSolar = solarQuiz.ShowDialog();

            if (okSolar == true)
            {
                SaveQuizResult(null, null, solarQuiz.FinalScore, solarQuiz.TotalQuestions);
                MessageBox.Show($"Score: {solarQuiz.FinalScore} / {solarQuiz.TotalQuestions}");
            }
        }

        // Saves quiz result summary (score/total) to DB.
        private void SaveQuizResult(int? planetId, int? itemId, int score, int total)
        {
            if (!AppState.IsLoggedIn || AppState.CurrentUser!.UserId <= 0)
            {
                MessageBox.Show("User not properly selected. Please login again.");
                return;
            }

            try
            {
                using var db = new PlanetContext();

                var result = new QuizResult
                {
                    PlanetId = planetId,
                    Score = score,
                    TotalQuestions = total,
                    Timestamp = DateTime.Now,
                    UserId = AppState.CurrentUser.UserId
                };

                db.QuizResults.Add(result);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot save results:\n\n" + ex.Message);
            }
        }

        // ===================== MEASURE TOOL =====================
        // Handles selecting first/second object while measure mode is enabled.
        private void HandleMeasurementSelection(string itemName)
        {
            if (!_spaceItemByName.TryGetValue(itemName.Trim(), out var item))
            {
                if (MeasureResultText != null)
                    MeasureResultText.Text = "Item not found in SpaceItems DB.";
                return;
            }

            if (_measureA == null)
            {
                _measureA = item;
                if (MeasureResultText != null)
                    MeasureResultText.Text = $"First: {_measureA.Name}. Now click second object...";
                return;
            }

            _measureB = item;

            // Prevent measuring item against itself.
            if (_measureB.ItemId == _measureA.ItemId)
            {
                if (MeasureResultText != null)
                    MeasureResultText.Text = "Pick a different second object.";

                _measureB = null;
                return;
            }

            ShowMeasurement(_measureA, _measureB);

            _measureA = null;
            _measureB = null;

            if (MeasureResultText != null)
                MeasureResultText.Text += "\nClick first object again...";
        }

        // Computes distance/time and updates UI; draws a line if 3D coordinates exist.
        private void ShowMeasurement(SpaceItem a, SpaceItem b)
        {
            var result = ComputeHybridDistanceKm(a, b);

            if (!result.Success)
            {
                if (MeasureResultText != null)
                    MeasureResultText.Text = result.ErrorMessage ?? "Cannot measure distance (missing data).";

                RemoveMeasurementLine();
                return;
            }

            double distanceKm = result.DistanceKm;
            double speedKmPerSec = GetSelectedSpeedKmPerSec();
            double seconds = distanceKm / speedKmPerSec;

            string timeText = FormatTravelTime(seconds);

            if (MeasureResultText != null)
            {
                MeasureResultText.Text =
                    $"Distance: {distanceKm:N0} km\n" +
                    $"Time @ {speedKmPerSec:N3} km/s: {timeText}\n" +
                    $"{a.Name} → {b.Name}";
            }

            if (result.UsedMethod == DistanceMethod.ThreeD)
                DrawMeasurementLine3D(a, b);
            else
                RemoveMeasurementLine();
        }

        // Hybrid distance: prefers full 3D XYZ if available; otherwise radial distance from the Sun.
        private DistanceResult ComputeHybridDistanceKm(SpaceItem a, SpaceItem b)
        {
            if (HasXYZ(a) && HasXYZ(b))
            {
                double dx = b.PositionXKm!.Value - a.PositionXKm!.Value;
                double dy = b.PositionYKm!.Value - a.PositionYKm!.Value;
                double dz = b.PositionZKm!.Value - a.PositionZKm!.Value;

                double distanceKm = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                return new DistanceResult
                {
                    Success = true,
                    DistanceKm = distanceKm,
                    UsedMethod = DistanceMethod.ThreeD
                };
            }

            if (a.DistanceFromSunKm.HasValue && b.DistanceFromSunKm.HasValue)
            {
                double distanceKm = Math.Abs(b.DistanceFromSunKm.Value - a.DistanceFromSunKm.Value);

                return new DistanceResult
                {
                    Success = true,
                    DistanceKm = distanceKm,
                    UsedMethod = DistanceMethod.RadialFromSun
                };
            }

            return new DistanceResult
            {
                Success = false,
                ErrorMessage =
                    "Missing position data.\n\n" +
                    "For planets: ensure SpaceItems.DistanceFromSunKm is filled.\n" +
                    "For deep-space objects: fill PositionXKm/PositionYKm/PositionZKm.\n\n" +
                    $"{a.Name}: DistanceFromSunKm={(a.DistanceFromSunKm.HasValue ? "OK" : "NULL")}, XYZ={(HasXYZ(a) ? "OK" : "NULL")}\n" +
                    $"{b.Name}: DistanceFromSunKm={(b.DistanceFromSunKm.HasValue ? "OK" : "NULL")}, XYZ={(HasXYZ(b) ? "OK" : "NULL")}"
            };
        }

        // Returns true if XYZ fields exist for the item.
        private static bool HasXYZ(SpaceItem s)
            => s.PositionXKm.HasValue && s.PositionYKm.HasValue && s.PositionZKm.HasValue;

        // Reads selected travel speed from UI (km/s), defaults to speed of light.
        private double GetSelectedSpeedKmPerSec()
        {
            if (SpeedCombo != null &&
                SpeedCombo.SelectedItem is ComboBoxItem cbi &&
                double.TryParse(cbi.Tag?.ToString(), out double v))
            {
                return v;
            }

            return 299792; // default: speed of light km/s
        }

        // Formats travel time into human-friendly units.
        private static string FormatTravelTime(double seconds)
        {
            if (seconds < 60) return $"{seconds:N1} seconds";
            double minutes = seconds / 60;
            if (minutes < 60) return $"{minutes:N1} minutes";
            double hours = minutes / 60;
            if (hours < 24) return $"{hours:N1} hours";
            double days = hours / 24;
            if (days < 365) return $"{days:N1} days";
            double years = days / 365;
            return $"{years:N2} years";
        }

        // Draws a 3D line between two XYZ positions (scaled into scene units).
        private void DrawMeasurementLine3D(SpaceItem a, SpaceItem b)
        {
            if (HelixView == null) return;

            RemoveMeasurementLine();

            double scale = DistanceScale;

            var p1 = new Point3D(
                a.PositionXKm!.Value * scale,
                a.PositionYKm!.Value * scale,
                a.PositionZKm!.Value * scale
            );

            var p2 = new Point3D(
                b.PositionXKm!.Value * scale,
                b.PositionYKm!.Value * scale,
                b.PositionZKm!.Value * scale
            );

            _measureLine = new LinesVisual3D
            {
                Thickness = 3,
                Color = Colors.Yellow,
                Points = new Point3DCollection { p1, p2 }
            };

            HelixView.Children.Add(_measureLine);
            _sceneVisuals.Add(_measureLine);
        }

        // Removes the measurement line visual if present.
        private void RemoveMeasurementLine()
        {
            if (HelixView == null) return;

            if (_measureLine != null)
            {
                if (HelixView.Children.Contains(_measureLine))
                    HelixView.Children.Remove(_measureLine);

                _sceneVisuals.Remove(_measureLine);
                _measureLine = null;
            }
        }

        // Clears selection state for measurement tool.
        private void ClearMeasurement()
        {
            _measureA = null;
            _measureB = null;
            RemoveMeasurementLine();
        }

        // ===================== MATERIALS =====================
        // Loads an image texture material safely; falls back to solid color on error.
        private Material MakeMaterialSafe(string? texturePath, Color fallbackColor)
        {
            string key = string.IsNullOrWhiteSpace(texturePath)
                ? $"__solid__{fallbackColor}"
                : texturePath.Trim();

            if (_materialCache.TryGetValue(key, out var cached))
                return cached;

            Material mat;

            if (string.IsNullOrWhiteSpace(texturePath))
            {
                var b = new SolidColorBrush(fallbackColor);
                b.Freeze();
                mat = new DiffuseMaterial(b);
                mat.Freeze();
                _materialCache[key] = mat;
                return mat;
            }

            try
            {
                var uri = new Uri($"pack://application:,,,/{texturePath}", UriKind.Absolute);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
                brush.Freeze();

                mat = new DiffuseMaterial(brush);
                mat.Freeze();
            }
            catch
            {
                var b = new SolidColorBrush(fallbackColor);
                b.Freeze();
                mat = new DiffuseMaterial(b);
                mat.Freeze();
            }

            _materialCache[key] = mat;
            return mat;
        }

        // Computes the visual radius for a planet using ratio lookup (clamped).
        private double GetVisualRadius(Planet p)
        {
            double ratio = _radiusRatio.TryGetValue(p.Name, out var rr) ? rr : 1.0;
            double radius = BasePlanetRadius * ratio;
            return Math.Min(radius, MaxPlanetRadius);
        }

        // Utility clamp.
        private static double Clamp(double v, double min, double max)
            => v < min ? min : (v > max ? max : v);

        // ===================== LOGGING / TIMING =====================
        // Starts timing for a planet view session.
        private void StartPlanetTiming(int planetId)
        {
            if (_activePlanetIdForTiming.HasValue && _activePlanetIdForTiming.Value != planetId)
                EndPlanetTimingIfRunning(reason: "SwitchPlanet");

            _activePlanetIdForTiming = planetId;
            _planetStopwatch.Restart();
            _logger.Log(planetId, "TimeStart", meta: $"mode={_viewMode}");
        }

        // Ends timing session and logs duration if meaningful.
        private void EndPlanetTimingIfRunning(string reason)
        {
            if (!_activePlanetIdForTiming.HasValue) return;

            _planetStopwatch.Stop();
            var seconds = _planetStopwatch.Elapsed.TotalSeconds;

            if (seconds >= 1.0)
            {
                _logger.Log(
                    _activePlanetIdForTiming.Value,
                    "TimeEnd",
                    durationSeconds: seconds,
                    meta: $"reason={reason};mode={_viewMode}"
                );
            }

            _activePlanetIdForTiming = null;
        }
    }

    // ===================== CAMERA ANIMATION SUPPORT TYPES =====================

    // Holds active camera animation state (from/to snapshots, timing).
    internal sealed class CameraAnimationState
    {
        public required PerspectiveCamera Camera { get; init; }
        public required CameraSnapshot From { get; init; }
        public required CameraSnapshot To { get; init; }
        public required DateTime StartUtc { get; init; }
        public required int DurationMs { get; init; }
    }

    // Captures camera transform values for interpolation.
    internal sealed class CameraSnapshot
    {
        public Point3D Position { get; init; }
        public Vector3D LookDirection { get; init; }
        public Vector3D UpDirection { get; init; }
        public double FieldOfView { get; init; }

        // Builds a snapshot from a camera.
        public static CameraSnapshot From(PerspectiveCamera cam) => new()
        {
            Position = cam.Position,
            LookDirection = cam.LookDirection,
            UpDirection = cam.UpDirection,
            FieldOfView = cam.FieldOfView
        };

        // Applies this snapshot back to a camera.
        public void ApplyTo(PerspectiveCamera cam)
        {
            cam.Position = Position;
            cam.LookDirection = LookDirection;
            cam.UpDirection = UpDirection;
            cam.FieldOfView = FieldOfView;
        }
    }
}
