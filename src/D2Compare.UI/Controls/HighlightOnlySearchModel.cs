using System.ComponentModel;
using Avalonia.Controls.DataGridSearching;

namespace D2Compare.Controls;

// SearchModel always makes the first match current, and the grid answers that with
// ScrollIntoView. On a large grouped view that walks every row group (20s+ on a heavy batch),
// so this wrapper keeps the highlighting but never exposes a current match.
public sealed class HighlightOnlySearchModel : ISearchModel
{
    private readonly SearchModel _inner = new();

    public HighlightOnlySearchModel(SearchHighlightMode highlightMode)
    {
        _inner.HighlightMode = highlightMode;
        _inner.HighlightCurrent = false;
        _inner.UpdateSelectionOnNavigate = false;
    }

    public IReadOnlyList<SearchDescriptor> Descriptors => _inner.Descriptors;
    public IReadOnlyList<SearchResult> Results => _inner.Results;

    public SearchHighlightMode HighlightMode
    {
        get => _inner.HighlightMode;
        set => _inner.HighlightMode = value;
    }

    public bool HighlightCurrent
    {
        get => false;
        set { }
    }

    public bool UpdateSelectionOnNavigate
    {
        get => false;
        set { }
    }

    public bool WrapNavigation
    {
        get => _inner.WrapNavigation;
        set => _inner.WrapNavigation = value;
    }

    public int CurrentIndex => -1;
    public SearchResult? CurrentResult => null;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _inner.PropertyChanged += value;
        remove => _inner.PropertyChanged -= value;
    }

    public event EventHandler<SearchChangedEventArgs>? SearchChanged
    {
        add => _inner.SearchChanged += value;
        remove => _inner.SearchChanged -= value;
    }

    public event EventHandler<SearchResultsChangedEventArgs>? ResultsChanged
    {
        add => _inner.ResultsChanged += value;
        remove => _inner.ResultsChanged -= value;
    }

    // Never raised: no current match means the grid never scrolls on search
    public event EventHandler<SearchCurrentChangedEventArgs>? CurrentChanged
    {
        add { }
        remove { }
    }

    public void Apply(IEnumerable<SearchDescriptor> descriptors) => _inner.Apply(descriptors);

    public void Clear() => _inner.Clear();

    public void SetOrUpdate(SearchDescriptor descriptor) => _inner.SetOrUpdate(descriptor);

    public bool MoveNext() => false;

    public bool MovePrevious() => false;

    public bool MoveTo(int index) => false;

    public void UpdateResults(IReadOnlyList<SearchResult> results) => _inner.UpdateResults(results);

    public void BeginUpdate() => _inner.BeginUpdate();

    public void EndUpdate() => _inner.EndUpdate();

    public IDisposable DeferRefresh() => _inner.DeferRefresh();
}
