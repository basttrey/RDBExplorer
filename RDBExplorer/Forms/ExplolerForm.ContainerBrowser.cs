using System.Drawing;
using RDBExplorer.Core.Models;

namespace RDBExplorer.Forms
{
    public partial class ExplolerForm
    {
        private const string AllContainersLabel = "All Containers";
        private const int MaxContainerMatches = 1000;
        private const string NoContainerMatchesLabel = "No matching containers";

        // The entries are read from the already loaded RDB index; no .fdata files are extracted.
        private string[] _containerNames = Array.Empty<string>();
        private string? _selectedContainerPath;
        private Panel? _containerPicker;
        private TextBox? _containerSearch;
        private Button? _containerArrow;
        private ToolStripDropDown? _containerPopup;
        private ListBox? _containerResults;
        private bool _updatingContainerText;

        private void SetupContainerBrowser()
        {
            // Keep the original table's list and status row. Reparent only the two
            // existing top-row controls into a three-column toolbar.
            tableLayoutPanel1.Controls.Remove(filterBox);
            tableLayoutPanel1.Controls.Remove(typeFilterComboBox);

            var filterToolbar = new TableLayoutPanel
            {
                Name = "ResourceFilterToolbar",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 3,
                RowCount = 1
            };
            filterToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            filterToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29F));
            filterToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            filterToolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            filterBox.Dock = DockStyle.Top;
            typeFilterComboBox.Dock = DockStyle.Top;
            filterToolbar.Controls.Add(filterBox, 0, 0);
            filterToolbar.Controls.Add(typeFilterComboBox, 1, 0);

            _containerPicker = new Panel
            {
                Name = "ContainerPicker",
                Dock = DockStyle.Top,
                Height = Math.Max(filterBox.Height, typeFilterComboBox.Height),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(3),
                BackColor = SystemColors.Window
            };
            var textHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(3, 3, 0, 0),
                BackColor = SystemColors.Window
            };
            _containerSearch = new TextBox
            {
                Name = "ContainerSearch",
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Text = AllContainersLabel,
                AccessibleName = "Search and select container"
            };
            _containerArrow = new Button
            {
                Name = "ContainerDropdownButton",
                Text = "▼",
                Dock = DockStyle.Right,
                Width = 24,
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            _containerArrow.FlatAppearance.BorderSize = 0;
            _containerArrow.Click += (_, _) =>
            {
                if (_containerPopup?.Visible == true)
                {
                    _containerPopup.Close();
                    return;
                }
                SetContainerSearchText("");
                ShowContainerPopup("");
                _containerSearch?.Focus();
            };
            textHost.Controls.Add(_containerSearch);
            _containerPicker.Controls.Add(textHost);
            _containerPicker.Controls.Add(_containerArrow);
            // Select the current choice for a fresh search, but not when focus
            // returns from the results popup in the middle of typing.
            _containerSearch.Enter += (_, _) =>
            {
                if (_containerPopup?.Visible != true)
                    _containerSearch.SelectAll();
            };
            _containerSearch.TextChanged += (_, _) =>
            {
                if (!_updatingContainerText)
                    ShowContainerPopup(_containerSearch.Text);
            };
            _containerSearch.KeyDown += ContainerSearch_KeyDown;
            filterToolbar.Controls.Add(_containerPicker, 2, 0);

            tableLayoutPanel1.Controls.Add(filterToolbar, 0, 0);
            tableLayoutPanel1.SetColumnSpan(filterToolbar, 2);

            _containerResults = new ListBox
            {
                Name = "ContainerSearchResults",
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                HorizontalScrollbar = true,
                Font = Font
            };
            _containerResults.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                int index = _containerResults.IndexFromPoint(e.Location);
                if (index >= 0 && index < _containerResults.Items.Count)
                {
                    _containerResults.SelectedIndex = index;
                    CommitContainerChoice();
                }
            };
            _containerResults.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    CommitContainerChoice();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _containerPopup?.Close();
                    e.SuppressKeyPress = true;
                }
            };

            var resultHost = new ToolStripControlHost(_containerResults)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _containerPopup = new ToolStripDropDown
            {
                AutoClose = false,
                AutoSize = false,
                Padding = new Padding(1),
                Margin = Padding.Empty
            };
            _containerPopup.Items.Add(resultHost);
            _containerPopup.Closed += (_, _) =>
            {
                if (!IsDisposed && _containerSearch is { IsDisposed: false })
                    RestoreContainerSelectionText();
            };
            // Close and restore the selected container when the user moves to another
            // resource control; leave the popup open while typing in the search box.
            archiveList.MouseDown += (_, _) => _containerPopup?.Close();
            filterBox.MouseDown += (_, _) => _containerPopup?.Close();
            typeFilterComboBox.MouseDown += (_, _) => _containerPopup?.Close();
            menuStrip1.MouseDown += (_, _) => _containerPopup?.Close();
            FormClosed += (_, _) => _containerPopup.Dispose();
        }

        private void PopulateContainerBrowser()
        {
            _containerPopup?.Close();
            _selectedContainerPath = null;
            _containerNames = _archiveExploler?.RDBEntries?
                .Select(e => e.Location?.ContainerPath)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            RestoreContainerSelectionText();
        }

        private void SetContainerSearchText(string value)
        {
            if (_containerSearch == null) return;
            _updatingContainerText = true;
            try
            {
                _containerSearch.Text = value;
                _containerSearch.SelectionStart = _containerSearch.TextLength;
            }
            finally { _updatingContainerText = false; }
        }

        private void RestoreContainerSelectionText() =>
            SetContainerSearchText(_selectedContainerPath ?? AllContainersLabel);

        private void ShowContainerPopup(string query)
        {
            if (_containerPicker == null || _containerResults == null ||
                _containerPopup == null || _archiveExploler == null || IsDisposed) return;

            // Typing searches container names only; the resource filter is changed
            // only after the user explicitly selects one of these results.
            string search = query.Trim();
            var matches = string.IsNullOrEmpty(search)
                ? _containerNames.Take(MaxContainerMatches).ToArray()
                : _containerNames.Where(name => name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .Take(MaxContainerMatches).ToArray();

            _containerResults.BeginUpdate();
            try
            {
                _containerResults.Items.Clear();
                if (search.Length == 0)
                    _containerResults.Items.Add(AllContainersLabel);
                _containerResults.Items.AddRange(matches.Cast<object>().ToArray());
            }
            finally { _containerResults.EndUpdate(); }

            if (_containerResults.Items.Count == 0)
                _containerResults.Items.Add(NoContainerMatchesLabel);
            int popupWidth = Math.Max(260, _containerPicker.Width);
            int popupHeight = Math.Min(290, _containerResults.ItemHeight * Math.Min(12, _containerResults.Items.Count) + 4);
            _containerResults.Size = new Size(popupWidth - 2, popupHeight - 2);
            if (_containerPopup.Items[0] is ToolStripControlHost host)
                host.Size = _containerResults.Size;
            _containerPopup.Size = new Size(popupWidth, popupHeight);
            if (!_containerPopup.Visible)
            {
                _containerPopup.Show(_containerPicker, new Point(0, _containerPicker.Height));
                // A ToolStripDropDown can claim keyboard focus when first shown.
                // Give focus back to the main-form TextBox so letters are not lost.
                _containerSearch?.Focus();
            }
        }

        private void ContainerSearch_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _containerPopup?.Close();
                RestoreContainerSelectionText();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                if (_containerPopup?.Visible == true && _containerResults?.Items.Count > 0)
                {
                    _containerResults.SelectedIndex = 0;
                    CommitContainerChoice();
                }
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down && _containerPopup?.Visible == true &&
                     _containerResults?.Items.Count > 0)
            {
                _containerResults.SelectedIndex = 0;
                _containerResults.Focus();
                e.SuppressKeyPress = true;
            }
        }

        private void CommitContainerChoice()
        {
            if (_containerResults?.SelectedItem is not string choice) return;
            if (choice == NoContainerMatchesLabel) return;
            string? newPath = choice == AllContainersLabel ? null : choice;
            // All results are taken from the RDB index: typing arbitrary text alone
            // can never select a nonexistent container.
            if (newPath != null && !_containerNames.Contains(newPath, StringComparer.OrdinalIgnoreCase)) return;
            bool changed = !string.Equals(_selectedContainerPath, newPath, StringComparison.OrdinalIgnoreCase);
            _selectedContainerPath = newPath;
            _containerPopup?.Close();
            RestoreContainerSelectionText();
            if (changed) ShowFiles(filterBox.Text);
            archiveList.Focus();
        }
    }
}
