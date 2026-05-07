using System.Windows;
using System.Windows.Input;

namespace DoneToday;

public partial class HotkeyDialog : Window
{
    public uint ResultModifiers { get; private set; }
    public uint ResultVirtualKey { get; private set; }
    public double ResultFontSize { get; private set; }
    public string ResultUpdateSource { get; private set; } = "";

    /// <summary>Invoked while the user drags the font-size slider, so the caller can live-preview.</summary>
    public Action<double>? FontSizePreview { get; set; }

    private bool _recording;

    public HotkeyDialog(uint currentModifiers, uint currentVk, double currentFontSize, string currentUpdateSource)
    {
        InitializeComponent();
        ResultModifiers = currentModifiers;
        ResultVirtualKey = currentVk;
        ResultFontSize = currentFontSize;
        ResultUpdateSource = currentUpdateSource;
        FontSizeSlider.Value = currentFontSize;
        FontSizeLabel.Text = $"{(int)currentFontSize} px";
        UpdateSourceBox.Text = currentUpdateSource;
        UpdateDisplay();

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionLabel.Text = v != null ? $"Done Today  v{v.Major}.{v.Minor}.{v.Build}" : "";

        Loaded += (_, __) => CaptureBox.Focus();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var src = UpdateSourceBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(src))
        {
            UpdateStatus.Text = "Enter a URL or folder path first.";
            return;
        }

        UpdateStatus.Text = "Checking…";
        var info = await UpdateChecker.CheckAsync(src);

        if (info == null)
        {
            UpdateStatus.Text = $"You're up to date  (v{UpdateChecker.CurrentVersion.Major}.{UpdateChecker.CurrentVersion.Minor}.{UpdateChecker.CurrentVersion.Build}).";
        }
        else
        {
            UpdateStatus.Text = $"Update available: v{info.Version}.  Close this dialog and use the banner to install.";
            // Tell the main window so it can show the banner
            if (Owner is MainWindow mw) mw.ShowUpdateBanner(info, src);
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontSizeLabel == null) return;
        ResultFontSize = e.NewValue;
        FontSizeLabel.Text = $"{(int)e.NewValue} px";
        FontSizePreview?.Invoke(e.NewValue);
    }

    private void UpdateDisplay()
    {
        if (ResultVirtualKey == 0)
            HotkeyText.Text = _recording ? "Press a key combination…" : "(none)";
        else
            HotkeyText.Text = HotkeyFormatter.Format(ResultModifiers, ResultVirtualKey);

        HintText.Text = _recording
            ? "Listening… press Esc to cancel recording."
            : "Click the box above to record a new combination.";
    }

    private void CaptureBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CaptureBox.Focus();
    }

    private void CaptureBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recording = true;
        CaptureBox.BorderBrush = (System.Windows.Media.Brush)FindResource("Brush.Accent");
        UpdateDisplay();
    }

    private void CaptureBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recording = false;
        CaptureBox.BorderBrush = (System.Windows.Media.Brush)FindResource("Brush.Border");
        UpdateDisplay();
    }

    private void CaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            // Cancel recording, defocus
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, null);
            this.Focus();
            return;
        }

        // Skip pure modifier keys — wait for an actual key
        if (key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
        {
            HintText.Text = "Combination must include Ctrl, Alt, Shift, or Win.";
            return;
        }

        uint w32mods = 0;
        if ((mods & ModifierKeys.Control) != 0) w32mods |= HotkeyFormatter.MOD_CONTROL;
        if ((mods & ModifierKeys.Alt) != 0)     w32mods |= HotkeyFormatter.MOD_ALT;
        if ((mods & ModifierKeys.Shift) != 0)   w32mods |= HotkeyFormatter.MOD_SHIFT;
        if ((mods & ModifierKeys.Windows) != 0) w32mods |= HotkeyFormatter.MOD_WIN;

        ResultModifiers = w32mods;
        ResultVirtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        UpdateDisplay();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ResultModifiers = 0;
        ResultVirtualKey = 0;
        UpdateDisplay();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultUpdateSource = UpdateSourceBox.Text.Trim();
        DialogResult = true;
    }

    private void License_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new LicenseDialog { Owner = this };
        dlg.ShowDialog();
    }
}
