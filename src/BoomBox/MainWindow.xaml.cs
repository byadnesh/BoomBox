using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BoomBox.Core;
using Microsoft.Win32;

namespace BoomBox;

public sealed record BoxRow(Box Box, string State, Brush StateBrush)
{
    public string Name => Box.Name;
    public string OsName => Box.OsName;
    public string Folder => Box.Folder;
    public bool IsRunning => State == "Running";
}

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(5) };
    private CancellationTokenSource? _creating;

    public MainWindow()
    {
        InitializeComponent();
        MenuCheck.IsChecked = ShellMenu.IsInstalled;
        MenuCheck.Checked += OnMenuToggled;
        MenuCheck.Unchecked += OnMenuToggled;
        _refresh.Tick += async (_, _) => await RefreshAsync();
        _refresh.Start();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var states = await Wsl.ListAsync();
        var running = (Brush)FindResource("Running");
        var stopped = (Brush)FindResource("Muted");
        var danger = (Brush)FindResource("Danger");

        BoxList.ItemsSource = BoxStore.List().Select(box =>
        {
            var state = states.GetValueOrDefault(box.Distro, "Missing");
            return new BoxRow(box, state, state switch
            {
                "Running" => running,
                "Missing" => danger,
                _ => stopped,
            });
        }).ToList();
        EmptyText.Visibility = BoxList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPathChanged(object sender, RoutedEventArgs e)
    {
        CreateButton.IsEnabled = _creating == null
            && FolderBox.Text.Trim().Length > 0
            && IsoBox.Text.Trim().Length > 0;
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder that will become a box" };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void OnBrowseIso(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a Linux Live ISO", Filter = "ISO images (*.iso)|*.iso" };
        if (dialog.ShowDialog(this) == true) IsoBox.Text = dialog.FileName;
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        _creating = new CancellationTokenSource();
        CreateButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ProgressPanel.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
        Progress.IsIndeterminate = true;

        var progress = new Progress<BoxProgress>(p =>
        {
            StageText.Text = p.Stage;
            Progress.IsIndeterminate = p.Percent == null;
            if (p.Percent is { } percent) Progress.Value = percent;
        });

        try
        {
            var (folder, iso, token) = (FolderBox.Text.Trim(), IsoBox.Text.Trim(), _creating.Token);
            // Off the UI thread: this moves gigabytes through a loop.
            await Task.Run(() => BoxCreator.CreateAsync(folder, iso, progress, token), CancellationToken.None);
            FolderBox.Clear();
            IsoBox.Clear();
        }
        catch (OperationCanceledException)
        {
            // Cancelling needs no message; the panel simply closes.
        }
        catch (BoomBoxException error)
        {
            ShowError(error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowError(error.Message);
        }
        finally
        {
            _creating.Dispose();
            _creating = null;
            ProgressPanel.Visibility = Visibility.Collapsed;
            OnPathChanged(this, e);
            await RefreshAsync();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        StageText.Text = "Cancelling and cleaning up";
        _creating?.Cancel();
    }

    private void OnTerminal(object sender, RoutedEventArgs e) => Launcher.OpenTerminal(RowOf(sender).Box);

    private void OnFolder(object sender, RoutedEventArgs e) => Launcher.OpenFolder(RowOf(sender).Box);

    private async void OnSystemFiles(object sender, RoutedEventArgs e)
    {
        var box = RowOf(sender).Box;
        // Explorer can only reach a distro that is awake.
        await Wsl.RunAsync(["-d", box.Distro, "-e", "true"]);
        Launcher.OpenSystemFiles(box);
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        await Wsl.RunAsync(["--terminate", RowOf(sender).Box.Distro]);
        await RefreshAsync();
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        var box = RowOf(sender).Box;
        var answer = MessageBox.Show(this,
            $"Delete the {box.OsName} system in \"{box.Name}\"?\n\n" +
            "Everything inside Linux outside /box is erased and cannot be recovered. " +
            "The files you can see in the folder stay.",
            "Delete box", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            await BoxCreator.DeleteAsync(box);
        }
        catch (BoomBoxException error)
        {
            ShowError(error.Message);
        }
        await RefreshAsync();
    }

    private void OnMenuToggled(object sender, RoutedEventArgs e)
    {
        if (MenuCheck.IsChecked == true) ShellMenu.Install(Environment.ProcessPath!);
        else ShellMenu.Uninstall();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static BoxRow RowOf(object sender) => (BoxRow)((FrameworkElement)sender).DataContext;
}
