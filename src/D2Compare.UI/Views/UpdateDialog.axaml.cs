using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;

using Avalonia.Controls;

using Markdown.Avalonia;

namespace D2Compare.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    public UpdateDialog(string version, string releaseNotes) : this()
    {
        Title = $"Update to {version}";

        var viewer = this.FindControl<MarkdownScrollViewer>("ChangelogViewer")!;
        viewer.Engine.HyperlinkCommand = new OpenLinkCommand();
        viewer.Markdown = string.IsNullOrWhiteSpace(releaseNotes)
            ? "No changelog available."
            : LinkifyUrls(releaseNotes);

        UpdateButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    // Convert bare URLs to markdown links, skipping ones already in [text](url) syntax
    private static partial class UrlPatterns
    {
        [GeneratedRegex(@"(?<!\]\()https?://[^\s\)]+", RegexOptions.IgnoreCase)]
        public static partial Regex BareUrl();
    }

    private static string LinkifyUrls(string text)
    {
        // Add extra spacing before the auto-generated "Full Changelog" line
        text = text.Replace("**Full Changelog**:", "\n\n---\n\nFull Changelog:");

        // Convert bare URLs to markdown links on their own line
        text = UrlPatterns.BareUrl().Replace(text, m => $"\n\n[{m.Value}]({m.Value})");
        return text;
    }

    private sealed class OpenLinkCommand : ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            if (parameter is string url && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
    }
}
