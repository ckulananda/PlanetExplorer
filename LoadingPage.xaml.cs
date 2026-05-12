using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PlanetExplorer
{
    public partial class LoadingPage : Page
    {
        private readonly DispatcherTimer _animTimer = new();
        private readonly Random _rng = new();

        private sealed class Star
        {
            public Ellipse Dot = null!;
            public double TwinkleSpeed;
        }

        private sealed class Streak
        {
            public Line Line = null!;
            public double Speed;
        }

        private readonly List<Star> _stars = new();
        private readonly List<Streak> _streaks = new();

        private int _stepIndex = 0;

        public LoadingPage()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                BuildStars(count: 160);
                BuildStreaks(count: 28);

                _animTimer.Interval = TimeSpan.FromMilliseconds(16);
                _animTimer.Tick += (_, __) =>
                {
                    AnimateStars();
                    AnimateStreaks();
                };
                _animTimer.Start();

                RunLoadingSequence();
            };

            Unloaded += (_, __) => _animTimer.Stop();
        }

        // -------------------- VISUALS --------------------

        private void BuildStars(int count)
        {
            StarCanvas.Children.Clear();
            _stars.Clear();

            // Ensure we have actual size
            var w = Math.Max(800, ActualWidth);
            var h = Math.Max(600, ActualHeight);

            for (int i = 0; i < count; i++)
            {
                double r = _rng.NextDouble() * 2.2 + 0.6;

                var e = new Ellipse
                {
                    Width = r,
                    Height = r,
                    Fill = new SolidColorBrush(Color.FromArgb(
                        (byte)_rng.Next(130, 255), 255, 255, 255))
                };

                Canvas.SetLeft(e, _rng.NextDouble() * w);
                Canvas.SetTop(e, _rng.NextDouble() * h);

                StarCanvas.Children.Add(e);

                _stars.Add(new Star
                {
                    Dot = e,
                    TwinkleSpeed = _rng.NextDouble() * 0.06 + 0.02
                });
            }
        }

        private void BuildStreaks(int count)
        {
            StreakCanvas.Children.Clear();
            _streaks.Clear();

            var w = Math.Max(800, ActualWidth);
            var h = Math.Max(600, ActualHeight);

            for (int i = 0; i < count; i++)
            {
                var line = new Line
                {
                    StrokeThickness = _rng.NextDouble() * 1.8 + 0.6,
                    Stroke = new SolidColorBrush(Color.FromArgb(
                        (byte)_rng.Next(40, 120), 170, 210, 255)),
                    X1 = _rng.NextDouble() * w,
                    Y1 = _rng.NextDouble() * h,
                    X2 = 0,
                    Y2 = 0
                };

                // streak direction (towards center)
                UpdateStreakEnd(line);

                StreakCanvas.Children.Add(line);

                _streaks.Add(new Streak
                {
                    Line = line,
                    Speed = _rng.NextDouble() * 18 + 10
                });
            }
        }

        private void UpdateStreakEnd(Line line)
        {
            // End point is closer to center to look like warp speed
            var w = Math.Max(800, ActualWidth);
            var h = Math.Max(600, ActualHeight);

            double cx = w / 2.0;
            double cy = h / 2.0;

            double dx = cx - line.X1;
            double dy = cy - line.Y1;

            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) len = 0.001;

            dx /= len;
            dy /= len;

            // streak length
            double L = _rng.NextDouble() * 80 + 40;

            line.X2 = line.X1 + dx * L;
            line.Y2 = line.Y1 + dy * L;
        }

        private void AnimateStars()
        {
            foreach (var s in _stars)
            {
                if (s.Dot.Fill is SolidColorBrush b)
                {
                    // twinkle alpha
                    int a = b.Color.A;
                    a += (_rng.Next(0, 2) == 0 ? -1 : 1) * (int)(s.TwinkleSpeed * 255);
                    a = Math.Max(90, Math.Min(255, a));

                    b.Color = Color.FromArgb((byte)a, 255, 255, 255);
                }
            }
        }

        private void AnimateStreaks()
        {
            var w = Math.Max(800, ActualWidth);
            var h = Math.Max(600, ActualHeight);

            foreach (var s in _streaks)
            {
                var line = s.Line;

                // Move start point outward away from center (reverse direction)
                double cx = w / 2.0;
                double cy = h / 2.0;

                double dx = line.X1 - cx;
                double dy = line.Y1 - cy;

                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.001) len = 0.001;

                dx /= len;
                dy /= len;

                line.X1 += dx * s.Speed;
                line.Y1 += dy * s.Speed;

                UpdateStreakEnd(line);

                // recycle if out of screen
                if (line.X1 < -200 || line.X1 > w + 200 || line.Y1 < -200 || line.Y1 > h + 200)
                {
                    line.X1 = _rng.NextDouble() * w;
                    line.Y1 = _rng.NextDouble() * h;
                    UpdateStreakEnd(line);
                }
            }
        }

        // -------------------- LOADING STEPS --------------------

        private async void RunLoadingSequence()
        {
            // Step list (you can rename)
            var steps = new (string title, string detail, Action work)[]
            {
                ("Loading resources...", "Loading textures, models, UI assets...", () => FakeWork(650)),
                ("Loading database...", "Checking PlanetExplorerDB connection...", () => TestDbConnection()),
                ("Connecting application...", "Preparing exploration modules...", () => FakeWork(650)),
                ("Welcome!", "Launching Start Menu...", () => FakeWork(450)),
            };

            for (int i = 0; i < steps.Length; i++)
            {
                StatusText.Text = steps[i].title;
                StepText.Text = steps[i].detail;

                LoadBar.Value = (i * 100.0) / steps.Length;

                // run work without freezing UI
                await Dispatcher.InvokeAsync(() => steps[i].work());
                await System.Threading.Tasks.Task.Delay(400);
            }

            LoadBar.Value = 100;

            // ✅ Navigate to Start Menu
            // If you don't have StartMenuPage yet, replace with your MainWindow open.
            var shell = Application.Current.MainWindow as ShellWindow;
            if (shell != null)
            {
                shell.Go(new StartMenuPage());
            }
            else
            {
                // fallback: open MainWindow
                var w = new MainWindow();
                Window.GetWindow(this)?.Close();
                w.Show();
            }
        }

        private void FakeWork(int ms)
        {
            // just waits a bit (simulate resource load)
            System.Threading.Thread.Sleep(ms);
        }

        private void TestDbConnection()
        {
            try
            {
                using var db = new PlanetContext();
                // simple query to test connection
                var ok = db.Planets.Any();
            }
            catch (Exception ex)
            {
                StepText.Text = "DB connection failed: " + ex.Message;
            }
        }
    }
}
