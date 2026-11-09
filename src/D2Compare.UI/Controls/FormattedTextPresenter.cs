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
        _colorizer.Resolve = ResolveBrush;
        _highlighter.IsDark = isDark;
        _editor.Foreground = ResolveBrush(SpanColor.Default);
        _editor.TextArea.TextView.Redraw();
    }

    // Same theme brushes as the grids (App.axaml), so both panels share one palette
    private IBrush ResolveBrush(SpanColor color)
    {
        var key = color switch
        {
            SpanColor.Header => "StatChangedBrush",
            SpanColor.Added => "StatAddedBrush",
            SpanColor.Removed => "StatRemovedBrush",
            SpanColor.FileName => "FileNameBrush",
            SpanColor.FileNameMuted => "MutedBrush",
            _ => "TextControlForeground",
        };
        return this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : Brushes.Gray;
    }

    // Applies span colors/weights; line N of the editor maps to FormattedDocument line N
    private sealed class SpanColorizer : DocumentColorizingTransformer
    {
        public IReadOnlyList<FormattedLine> Lines { get; set; } = [];
        public Func<SpanColor, IBrush> Resolve { get; set; } = _ => Brushes.Gray;

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

                var brush = Resolve(span.Color);
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
                                new Typeface(props.Typeface.FontFamily, weight: FontWeight.SemiBold)
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
            var background = IsDark ? HighlightDark : HighlightLight;
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

    private static readonly IBrush HighlightLight = new ImmutableSolidColorBrush(
        Color.FromRgb(0xFF, 0xF1, 0xA8)
    );
    private static readonly IBrush HighlightDark = new ImmutableSolidColorBrush(
        Color.FromRgb(0x5C, 0x4A, 0x12)
    );
}
