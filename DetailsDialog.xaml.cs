using System.Windows;
using System.Windows.Input;

namespace DoneToday;

public partial class DetailsDialog : Window
{
    public string ResultTitle { get; private set; }
    public string ResultDetails { get; private set; }

    public DetailsDialog(string title, string details)
    {
        InitializeComponent();
        TitleBox.Text = title;
        NotesBox.Text = details;
        ResultTitle = title;
        ResultDetails = details;
        Loaded += (_, __) =>
        {
            // Focus notes if title already has content; otherwise focus title
            if (string.IsNullOrWhiteSpace(title))
            {
                TitleBox.Focus();
            }
            else
            {
                NotesBox.Focus();
                NotesBox.CaretIndex = NotesBox.Text.Length;
            }
        };
    }

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter in title moves to notes rather than submitting (notes is multi-line)
        if (e.Key == Key.Enter)
        {
            NotesBox.Focus();
            NotesBox.CaretIndex = NotesBox.Text.Length;
            e.Handled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultTitle = TitleBox.Text.Trim();
        ResultDetails = NotesBox.Text.TrimEnd();
        DialogResult = true;
    }
}
