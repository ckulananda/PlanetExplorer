using System.Windows;
using System.Windows.Controls;

namespace PlanetExplorer
{
    public partial class ShellWindow : Window
    {
        public ShellWindow()
        {
            InitializeComponent();
            MainFrame.Navigate(new LoadingPage());
        }

        public void Go(Page page) => MainFrame.Navigate(page);
    }
}
