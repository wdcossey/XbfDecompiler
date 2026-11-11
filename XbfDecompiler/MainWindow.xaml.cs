using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using LibXbf;
using LibXbf.Output;
using Microsoft.Win32;

namespace XbfDecompiler;

public partial class MainWindow : Window
{
    private const string XbfFilter = "XAML Binary Files (*.xbf)|*.xbf|All files (*.*)|*.*";

    private readonly string? _initialPath;
    private XDocument? _xaml;
    private string? _currentPath;
    private bool _isLoading;

    public MainWindow(string? initialPath = null)
    {
        InitializeComponent();
        _initialPath = initialPath;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialPath is not null)
        {
            await LoadAsync(_initialPath);
        }
    }

    private void CanOpen(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_isLoading;

    private void HasDocument(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _xaml is not null && !_isLoading;

    private async void OnOpen(object sender, ExecutedRoutedEventArgs e)
    {
        // Dialogs must be shown on the UI thread; only the parsing is moved off it.
        var dialog = new OpenFileDialog
        {
            Filter = XbfFilter,
            CheckFileExists = true,
        };

        if (_currentPath is not null)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_currentPath);
        }

        if (dialog.ShowDialog(this) == true)
        {
            await LoadAsync(dialog.FileName);
        }
    }

    private void OnSave(object sender, ExecutedRoutedEventArgs e)
    {
        if (_xaml is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "XAML files (*.xaml)|*.xaml|All files (*.*)|*.*",
            DefaultExt = ".xaml",
            AddExtension = true,
            FileName = _currentPath is null ? "" : Path.GetFileNameWithoutExtension(_currentPath) + ".xaml",
            InitialDirectory = _currentPath is null ? "" : Path.GetDirectoryName(_currentPath),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            using var stream = File.Create(dialog.FileName);
            XamlOutput.Save(_xaml, stream);
            StatusText.Text = $"Saved {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not save file", ex.Message);
        }
    }

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        if (XamlText.Text.Length > 0)
        {
            Clipboard.SetText(XamlText.Text);
            StatusText.Text = "XAML copied to the clipboard.";
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_isLoading && GetDroppedFile(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!_isLoading && GetDroppedFile(e) is { } path)
        {
            e.Handled = true;
            await LoadAsync(path);
        }
    }

    private static string? GetDroppedFile(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && File.Exists(files[0]) ? files[0] : null;

    private async Task LoadAsync(string path)
    {
        SetLoading(true);
        StatusText.Text = $"Loading {path}…";

        try
        {
            // Parse and build both views off the UI thread.
            var (file, tree, metadata, xaml, xamlText) = await Task.Run(async () =>
            {
                XbfFile file = await XbfFile.LoadAsync(path).ConfigureAwait(false);
                XDocument xaml = new XamlOutput().GetOutput(file);
                return (file, new TreeOutput().GetOutput(file), new MetadataOutput().GetOutput(file), xaml,
                    XamlOutput.ToXamlString(xaml));
            });

            _currentPath = path;
            _xaml = xaml;
            XbfTree.ItemsSource = new[] { tree, metadata };
            XamlText.Text = xamlText;
            XamlText.ScrollToHome();
            Title = $"{Path.GetFileName(path)} - XBF Decompiler";
            StatusText.Text = $"{path}  |  XBF v{file.FileVersion}  |  {file.StringTable.Count} strings, " +
                              $"{file.TypeTable.Count} types, {file.PropertyTable.Count} properties";
        }
        catch (InvalidXbfException ex)
        {
            // Includes UnsupportedXbfVersionException, whose message explains which versions are supported.
            ShowError("Invalid file", $"{Path.GetFileName(path)} could not be decompiled.\n\n{ex.Message}");
            RestoreStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not open file", ex.Message);
            RestoreStatus();
        }
        catch (Exception ex)
        {
            ShowError("Unexpected error", ex.ToString());
            RestoreStatus();
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void RestoreStatus() =>
        StatusText.Text = _currentPath is null ? "Open or drop a Windows 8.1 / Windows Phone 8.1 .xbf file." : _currentPath;

    private void SetLoading(bool loading)
    {
        _isLoading = loading;
        Cursor = loading ? Cursors.Wait : null;
        CopyButton.IsEnabled = !loading && _xaml is not null;
        CommandManager.InvalidateRequerySuggested();
    }

    private void ShowError(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
