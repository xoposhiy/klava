using System.ComponentModel;

namespace Klava.App;

internal sealed class TableViewState
{
    private readonly object? _currentKey, _topKey;
    private readonly int _currentIndex, _columnIndex, _topIndex, _horizontalOffset;
    private readonly bool _selected;
    private readonly DataGridViewColumn? _sortColumn;
    private readonly SortOrder _sortOrder;

    public TableViewState(DataGridView table, Func<DataGridViewRow, object?> key)
    {
        _currentKey = table.CurrentRow is { } current ? key(current) : null;
        _currentIndex = table.CurrentRow?.Index ?? -1;
        _columnIndex = table.CurrentCell?.ColumnIndex ?? 0;
        _selected = table.CurrentRow?.Selected ?? false;
        _topIndex = table.FirstDisplayedScrollingRowIndex;
        _topKey = _topIndex >= 0 ? key(table.Rows[_topIndex]) : null;
        _horizontalOffset = table.HorizontalScrollingOffset;
        _sortColumn = table.SortedColumn;
        _sortOrder = table.SortOrder;
    }

    public void Restore(DataGridView table, Func<DataGridViewRow, object?> key)
    {
        if (_sortColumn is not null && _sortOrder != SortOrder.None)
            table.Sort(_sortColumn, _sortOrder == SortOrder.Ascending
                ? ListSortDirection.Ascending : ListSortDirection.Descending);
        if (table.Rows.Count == 0) return;
        DataGridViewRow Find(object? value, int fallback) =>
            table.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => Equals(key(row), value))
            ?? table.Rows[Math.Clamp(fallback, 0, table.Rows.Count - 1)];
        if (_currentIndex >= 0)
        {
            var current = Find(_currentKey, _currentIndex);
            table.CurrentCell = current.Cells[_columnIndex];
            table.ClearSelection();
            current.Selected = _selected;
        }
        // Setting CurrentCell scrolls to the selection; restore the viewport afterward.
        if (_topIndex >= 0) table.FirstDisplayedScrollingRowIndex = Find(_topKey, _topIndex).Index;
        table.HorizontalScrollingOffset = _horizontalOffset;
    }
}
