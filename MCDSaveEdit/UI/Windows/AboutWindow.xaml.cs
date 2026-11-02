using MCDSaveEdit.Data;
using MCDSaveEdit.Services;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace MCDSaveEdit.UI
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            Title = R.ABOUT_WINDOW_TITLE;
            versionLabel.Content = R.formatVERSION(Config.instance.versionLabel(), Constants.CURRENT_VERSION.ToString());
        }

        private void patreonButton_Click(object sender, RoutedEventArgs e)
        {
            EventLogger.logEvent("patreonButton_Click");
            Process.Start(new ProcessStartInfo("https://www.patreon.com/cutflame") { UseShellExecute = true });
            this.Close();
        }
        private void buyMeACoffeeButton_Click(object sender, RoutedEventArgs e)
        {
            EventLogger.logEvent("buyMeACoffeeButton_Click");
            Process.Start(new ProcessStartInfo("https://www.buymeacoffee.com/cutflame") { UseShellExecute = true });
            this.Close();
        }
        private void koFiButton_Click(object sender, RoutedEventArgs e)
        {
            EventLogger.logEvent("koFiButton_Click");
            Process.Start(new ProcessStartInfo("https://ko-fi.com/cutflame") { UseShellExecute = true });
            this.Close();
        }

    }
}
