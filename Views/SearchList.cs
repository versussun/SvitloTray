using SvitloTray.Core;

namespace SvitloTray.Views;

/// <summary>Search field + list of options (for settlement, street, house).</summary>
public sealed class SearchList : VerticalStackLayout
{
    readonly Label _titleLabel;
    readonly Label _selectedLabel;
    readonly Entry _entry;
    readonly CollectionView _list;
    readonly Label _status;
    IReadOnlyList<string> _all = Array.Empty<string>();
    Func<string>? _statusText;

    public string? Selected { get; private set; }
    public event Action<string>? ItemSelected;

    public SearchList()
    {
        Spacing = 4;

        _titleLabel = new Label { FontAttributes = FontAttributes.Bold, FontSize = 14 };
        _selectedLabel = new Label { FontSize = 13, TextColor = Colors.Gray };
        _status = new Label { FontSize = 12, TextColor = Colors.Gray, IsVisible = false };
        _entry = new Entry { ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
        _entry.TextChanged += (_, e) => Filter(e.NewTextValue);

        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            HeightRequest = 150,
            ItemTemplate = new DataTemplate(() =>
            {
                // An item is just a string; no bindings, so there is no dependency on reflection.
                var label = new Label { Padding = new Thickness(8, 5), FontSize = 14 };
                label.BindingContextChanged += (sender, _) =>
                {
                    if (sender is Label l) l.Text = l.BindingContext as string;
                };
                return label;
            }),
        };
        _list.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is string value) Choose(value, raise: true);
        };

        Add(_titleLabel);
        Add(_selectedLabel);
        Add(_entry);
        Add(_status);
        Add(_list);
    }

    /// <summary>Set (or re-apply after a language change) the title and the placeholder.</summary>
    public void SetTexts(string title, string placeholder)
    {
        _titleLabel.Text = title;
        _entry.Placeholder = placeholder;
        Preselect(Selected);
        if (_statusText is not null) _status.Text = _statusText();
    }

    public void SetItems(IReadOnlyList<string> items)
    {
        _all = items;
        _statusText = null;
        _status.IsVisible = false;
        Filter(_entry.Text);
    }

    /// <summary>Show a status line instead of the list; the text is re-evaluated when the language changes.</summary>
    public void SetStatus(Func<string> text)
    {
        _all = Array.Empty<string>();
        _list.ItemsSource = null;
        _statusText = text;
        _status.Text = text();
        _status.IsVisible = true;
    }

    /// <summary>Show the current value without raising an event.</summary>
    public void Preselect(string? value)
    {
        Selected = string.IsNullOrWhiteSpace(value) ? null : value;
        _selectedLabel.Text = Selected is null ? S.NotSelected : S.SelectedValue(Selected);
    }

    public new void Clear()
    {
        Preselect(null);
        _entry.Text = "";
        SetStatus(() => S.SelectAboveFirst);
    }

    void Choose(string value, bool raise)
    {
        Preselect(value);
        if (raise) ItemSelected?.Invoke(value);
    }

    void Filter(string? query)
    {
        query = (query ?? "").Trim();
        IEnumerable<string> items = _all;
        if (query.Length > 0)
        {
            items = _all
                .Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        }
        _list.ItemsSource = items.Take(300).ToList();
    }
}
