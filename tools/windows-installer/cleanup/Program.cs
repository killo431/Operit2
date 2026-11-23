using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Operit.Installer
{
    public static class Program
    {
        /// <summary>Opens a standalone cleanup tool that does not require Flutter or an installed MSI.</summary>
        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.Run(new CleanupWindow());
        }
    }

    public sealed class CleanupWindow : Window
    {
        private readonly UserDataCleanup cleanup;
        private readonly TextBox details;
        private readonly Button remove;
        private readonly CheckBox consent;
        private CleanupPlan plan;
        private bool running;

        /// <summary>Lists resolved roots and requires explicit consent before enabling permanent deletion.</summary>
        public CleanupWindow()
        {
            Title = "Operit2 User Data Cleanup";
            Width = 760;
            Height = 640;
            MinWidth = 620;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            FontSize = 13;
            Background = System.Windows.Media.Brushes.White;
            var panel = new DockPanel { Margin = new Thickness(26), Background = System.Windows.Media.Brushes.White };
            Content = panel;
            var heading = new TextBlock { Text = "Clean up local Operit2 user data", FontSize = 23, Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(heading, Dock.Top);
            panel.Children.Add(heading);
            var actions = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            DockPanel.SetDock(actions, Dock.Bottom);
            panel.Children.Add(actions);
            consent = new CheckBox { Content = "I have backed up the data I need and understand this affects every Operit2 copy on this PC.", Margin = new Thickness(0, 0, 0, 14) };
            actions.Children.Add(consent);
            remove = new Button { Content = "Permanently delete the data above", Height = 42, IsEnabled = false,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(54, 97, 142)),
                Foreground = System.Windows.Media.Brushes.White };
            actions.Children.Add(remove);
            details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) };
            panel.Children.Add(details);
            cleanup = new UserDataCleanup(AppDomain.CurrentDomain.BaseDirectory);
            try
            {
                plan = cleanup.CreatePlan();
                details.Text = plan.Describe() + "\n\nExit all Operit2 windows and background processes first.\n" +
                    "This tool only cleans data; uninstall the MSI install through system settings, and the portable version's program folder can be deleted afterward.";
            }
            catch (Exception error) { details.Text = "Cannot determine a safe cleanup scope:\n" + error.Message; }
            consent.Checked += ConsentChanged;
            consent.Unchecked += ConsentChanged;
            remove.Click += DeleteData;
            Closing += PreventClosingWhileRunning;
        }

        /// <summary>Enables deletion only for a resolved plan with explicit user consent.</summary>
        private void ConsentChanged(object sender, RoutedEventArgs args)
        {
            remove.IsEnabled = plan != null && consent.IsChecked == true && !running;
        }

        /// <summary>Confirms irreversible deletion and keeps failures visible without claiming success.</summary>
        private async void DeleteData(object sender, RoutedEventArgs args)
        {
            if (MessageBox.Show(this, "Permanently delete all local data listed here? This cannot be undone.", "Confirm cleanup",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            running = true;
            remove.IsEnabled = false;
            consent.IsEnabled = false;
            try
            {
                await Task.Run(() => cleanup.DeleteData(plan));
                details.Text = "User data cleanup complete. Data on other devices and in the cloud was not modified.\n" +
                    "Running any Operit2 copy again recreates the local data.";
                consent.Visibility = Visibility.Collapsed;
                remove.Content = "Cleanup complete";
                plan = null;
            }
            catch (Exception error)
            {
                details.Text = "Cleanup did not fully complete; deleted data cannot be recovered.\n" + error.Message + "\n\n" + plan.Describe();
                consent.IsEnabled = true;
            }
            finally { running = false; ConsentChanged(this, new RoutedEventArgs()); }
        }

        /// <summary>Keeps the process alive until its filesystem operation has finished.</summary>
        private void PreventClosingWhileRunning(object sender, System.ComponentModel.CancelEventArgs args)
        {
            args.Cancel = running;
        }
    }
}
