using System;
using System.IO;
using System.Windows;

namespace DoneToday;

public partial class LicenseDialog : Window
{
    public LicenseDialog()
    {
        InitializeComponent();
        LicenseText.Text = LoadLicenseText();
    }

    private static string LoadLicenseText()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/LICENSE", UriKind.Absolute);
            var resInfo = Application.GetResourceStream(uri);
            if (resInfo == null) return "(license text unavailable)";
            using var reader = new StreamReader(resInfo.Stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"(failed to load license: {ex.Message})";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
