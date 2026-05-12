using System.Windows;
using System.Windows.Controls;

namespace PlanetExplorer
{
    public partial class StartMenuPage : Page
    {
        public StartMenuPage() => InitializeComponent();

        private void StartExploration_Click(object sender, RoutedEventArgs e)
        {
            // for now: open your existing MainWindow
            // Later we convert MainWindow -> ExplorationPage
            var w = new MainWindow();
            w.Show();
        }

        private void Profile_Click(object sender, RoutedEventArgs e)
        {
            // reuse your existing ProfileWindow for now
            var w = new ProfileWindow();
            w.ShowDialog();
        }

        private void Progress_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Progress page next.");

        private void Settings_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("Settings page next.");

        private void About_Click(object sender, RoutedEventArgs e)
            => MessageBox.Show("About page next.");

        private void Exit_Click(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();
    }
}
