using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using D2Compare.Core.Models;

namespace D2Compare.Controls;

// Read-only, virtualized view of a FormattedDocument. AvaloniaEdit only renders visible
// lines, so large batch results stay responsive and search/theme changes only redraw.
public class FormattedTextPresenter : UserControl
{
    public static readonly StyledProperty<FormattedDocument?> DocumentProperty =
        AvaloniaProperty.Register<FormattedTextPresenter, FormattedDocument?>(nameof(Document));

    public static readonly StyledProperty<string?> SearchTermProperty = AvaloniaProperty.Register<
        FormattedTextPresenter,
        string?
    >(nameof(SearchTerm));

    private const double BodyFontSize = 13;

    private readonly TextEditor _editor;
    private readonly SpanColorizer _colorizer = new();
    private readonly SearchHighlighter _highlighter = new();

    public FormattedDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public string? SearchTerm
    {
        get => GetValue(SearchTermProperty);
        set => SetValue(SearchTermProperty, value);
    }

    public FormattedTextPresenter()
    {
        _editor = new TextEditor
        {
            IsReadOnly = true,
            WordWrap = false,
            FontSize = BodyFontSize,
            Background = Brushes.Transparent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Options = new TextEditorOptions
            {
                EnableHyperlinks = false,
                EnableEmailHyperlinks = false,
            },
        };
        _editor.TextArea.Caret.CaretBrush = Brushes.Transparent;

        var lineTransformers = _editor.TextArea.TextView.LineTransformers;
        lineTransformers.Add(_colorizer);
        lineTransformers.Add(_highlighter);

        Content = _editor;
        ActualThemeVariantChanged += (_, _) => Redraw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentProperty)
            LoadDocument();
        else if (change.Property == SearchTermProperty)
        {
            _highlighter.Term = SearchTerm;
            Redraw();
        }
    }

    private void LoadDocument()
    {
        var lines = Document?.Lines ?? [];
        _colorizer.Lines = lines;

        var text = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0)
                text.Append('\n');
            foreach (var span in lines[i].Spans)
                text.Append(span.Text);
        }

        _editor.Document = new TextDocument(text.ToString());
        Redraw();
    }

    private void Redraw()
    {
        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        _colorizer.IsDark = isDark;
        _highlighter.IsDark = isDark;
        _editor.Foreground = Palette.Resolve(SpanColor.Default, isDark);
        _editor.TextArea.TextView.Redraw();
    }

    // Applies span colors/weights; line N of the editor maps to FormattedDocument line N
    private sealed class SpanColorizer : DocumentColorizingTransformer
    {
        public IReadOnlyList<FormattedLine> Lines { get; set; } = [];
        public bool IsDark { get; set; }

        protected override void ColorizeLine(DocumentLine line)
        {
            var index = line.LineNumber - 1;
            if (index >= Lines.Count)
                return;

            var offset = line.Offset;
            foreach (var span in Lines[index].Spans)
            {
                if (span.Text.Length == 0)
                    continue;

                var brush = Palette.Resolve(span.Color, IsDark);
                var bold = span.Style == SpanStyle.Bold;
                var isFileName = span.Color is SpanColor.FileName or SpanColor.FileNameMuted;

                ChangeLinePart(
                    offset,
                    offset + span.Text.Length,
                    element =>
                    {
                        var props = element.TextRunProperties;
                        props.SetForegroundBrush(brush);
                        if (bold)
                            props.SetTypeface(
                                new Typeface(props.Typeface.FontFamily, weight: FontWeight.Bold)
                            );
                        if (isFileName)
                            props.SetFontRenderingEmSize(15);
                    }
                );
                offset += span.Text.Length;
            }
        }
    }

    // Separate pass so ChangeLinePart offsets stay increasing within each transformer
    private sealed class SearchHighlighter : DocumentColorizingTransformer
    {
        public string? Term { get; set; }
        public bool IsDark { get; set; }

        protected override void ColorizeLine(DocumentLine line)
        {
            if (string.IsNullOrEmpty(Term))
                return;

            var text = CurrentContext.Document.GetText(line);
            var background = IsDark ? Palette.HighlightDark : Palette.HighlightLight;
            var index = text.IndexOf(Term, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var start = line.Offset + index;
                ChangeLinePart(
                    start,
                    start + Term.Length,
                    element =>
                    {
                        element.TextRunProperties.SetBackgroundBrush(background);
                        if (IsDark)
                            element.TextRunProperties.SetForegroundBrush(Brushes.White);
                    }
                );
                index = text.IndexOf(Term, index + Term.Length, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static class Palette
    {
        private static readonly IBrush s_headerLight = Brush(25, 25, 112);
        private static readonly IBrush s_headerDark = Brush(65, 105, 225);
        private static readonly IBrush s_addedDark = Brush(80, 200, 120);
        private static readonly IBrush s_removedDark = Brush(255, 100, 100);
        private static readonly IBrush s_fileNameLight = Brush(255, 140, 0);
        private static readonly IBrush s_fileNameDark = Brush(255, 180, 70);
        private static readonly IBrush s_fileNameMuted = Brush(150, 150, 150);
        private static readonly IBrush s_defaultDark = Brush(220, 220, 220);

        public static readonly IBrush HighlightLight = Brushes.Yellow;
        public static readonly IBrush HighlightDark = Brush(80, 80, 0);

        public static IBrush Resolve(SpanColor color, bool isDark) =>
            color switch
            {
                SpanColor.Header => isDark ? s_headerDark : s_headerLight,
                SpanColor.Added => isDark ? s_addedDark : Brushes.Green,
                SpanColor.Removed => isDark ? s_removedDark : Brushes.Red,
                SpanColor.FileName => isDark ? s_fileNameDark : s_fileNameLight,
                SpanColor.FileNameMuted => s_fileNameMuted,
                _ => isDark ? s_defaultDark : Brushes.Black,
            };

        private static IBrush Brush(byte r, byte g, byte b) =>
            new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));
    }
}
