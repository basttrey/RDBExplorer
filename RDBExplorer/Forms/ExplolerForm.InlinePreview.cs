using RDBExplorer.Core;
using RDBExplorer.Core.Formats.G1T;
using RDBExplorer.Core.Models;
using RDBExplorer.Services;
using RDBExplorer.Utils;
using System.Drawing;

namespace RDBExplorer.Forms
{
    // Read-only, in-memory preview. The resource list and the original G1Tool remain unchanged.
    public partial class ExplolerForm
    {
        private SplitContainer? _previewSplit;
        private CheckBox? _browseTexturesCheck;
        private CheckBox? _g1tOnlyNavigationCheck;
        private Button? _previousTextureButton;
        private Button? _nextTextureButton;
        private PictureBox? _inlinePicture;
        private Label? _inlineMessage;
        private Label? _inlineHeader;
        private Label? _inlineDetails;
        private System.Windows.Forms.Timer? _previewDebounce;

        private long _inlineVersion;
        private bool _inlineDisposed;
        private bool _selectLastOnLoad;
        private bool _internalSelectionChange;
        private bool _inlineLoadFailed;
        private int _pendingTextureSteps; // Vertical, across textures and G1T files.
        private int _pendingLocalTextureSteps; // Horizontal, inside the selected G1T only.
        private int _inlineTextureIndex;
        private RDBEntry? _queuedEntry;
        private RDBEntry? _loadedEntry;
        private G1TParser? _inlineParser;

        private bool IsInlineRequestCurrent(long version, RDBEntry entry) =>
            !_inlineDisposed && !IsDisposed && version == _inlineVersion &&
            ReferenceEquals(_queuedEntry, entry);

        private static bool IsG1TResource(RDBEntry entry)
        {
            KTFileType type = (KTFileType)entry.TypeInfoKtid;
            return type == KTFileType.TexContext || type == KTFileType.StreamingTexContext ||
                   (entry.Name?.EndsWith(".g1t", StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (entry.Name?.EndsWith(".g1ts", StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private void SetupInlinePreview()
        {
            _previewSplit = new SplitContainer
            {
                Name = "G1TInlinePreviewSplit",
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                Size = new Size(1000, 600),
                SplitterDistance = 600,
                Panel1MinSize = 300,
                Panel2MinSize = 230,
                SplitterWidth = 5
            };

            // Keep the menu in its OWN row. Docking a Fill SplitContainer directly on
            // the Form can cover the first row of the left pane (Search / Type Filter)
            // and the preview header. BringToFront does not reserve space for a menu.
            // Reparent the existing controls; preserve all Designer events and handlers.
            Controls.Remove(menuStrip1);
            Controls.Remove(tableLayoutPanel1);
            _previewSplit.Panel1.Controls.Add(tableLayoutPanel1);

            var mainLayout = new TableLayoutPanel
            {
                Name = "ExplorerMainLayout",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                ColumnCount = 1,
                RowCount = 2
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            menuStrip1.Dock = DockStyle.Fill;
            menuStrip1.Margin = Padding.Empty;
            _previewSplit.Margin = Padding.Empty;
            mainLayout.Controls.Add(menuStrip1, 0, 0);
            mainLayout.Controls.Add(_previewSplit, 0, 1);
            Controls.Add(mainLayout);
            MainMenuStrip = menuStrip1;

            // The preview is ADDITIONAL workspace, not a replacement for list width.
            // Leave room for both panes when the monitor is wide enough.
            int availableWidth = Screen.FromControl(this).WorkingArea.Width;
            int expandedWidth = Math.Min(availableWidth, ClientSize.Width + 340);
            if (expandedWidth > ClientSize.Width)
                ClientSize = new Size(expandedWidth, ClientSize.Height);

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(5),
                BackColor = SystemColors.Control
            };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            _previewSplit.Panel2.Controls.Add(right);

            _inlineHeader = new Label
            {
                Text = "G1T Preview",
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = new Font(Font, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            right.Controls.Add(_inlineHeader, 0, 0);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0)
            };
            _browseTexturesCheck = new CheckBox
            {
                Name = "BrowseTexturesCheckBox",
                Text = "Browse Textures",
                AutoSize = true,
                Margin = new Padding(2, 7, 8, 2)
            };
            _browseTexturesCheck.CheckedChanged += (_, _) =>
            {
                // Switching modes does not change the current image. The first/last
                // texture is chosen only when the selected G1T resource changes.
                _pendingTextureSteps = 0;
                _pendingLocalTextureSteps = 0;
                _selectLastOnLoad = false;
                UpdateInternalNavigationButtons();
                archiveList.Focus();
            };
            toolbar.Controls.Add(_browseTexturesCheck);

            // Off + Browse Textures off: normal ListView up/down (all file types).
            // On + Browse Textures off: jump directly between G1T files.
            // Browse Textures on: up/down traverse all textures across G1T files.
            _g1tOnlyNavigationCheck = new CheckBox
            {
                Name = "G1TOnlyNavigationCheckBox",
                Text = "G1T Only",
                AutoSize = true,
                Margin = new Padding(0, 7, 6, 2)
            };
            _g1tOnlyNavigationCheck.CheckedChanged += (_, _) => archiveList.Focus();
            toolbar.Controls.Add(_g1tOnlyNavigationCheck);
            var navigationTips = new ToolTip();
            navigationTips.SetToolTip(_browseTexturesCheck,
                "Up/Down: browse textures sequentially across G1T files. Off: jump between files when G1T Only is checked.");
            navigationTips.SetToolTip(_g1tOnlyNavigationCheck,
                "Skip non-G1T files with Up/Down. Uncheck both boxes for ordinary file-list navigation.");

            _previousTextureButton = new Button
            {
                Name = "PreviousInternalTextureButton", Text = "◀", Width = 30, Height = 27,
                Margin = new Padding(0, 3, 2, 0), Enabled = false
            };
            _nextTextureButton = new Button
            {
                Name = "NextInternalTextureButton", Text = "▶", Width = 30, Height = 27,
                Margin = new Padding(0, 3, 2, 0), Enabled = false
            };
            _previousTextureButton.Click += (_, _) =>
            {
                NavigateWithinSelectedG1T(-1);
                archiveList.Focus();
            };
            _nextTextureButton.Click += (_, _) =>
            {
                NavigateWithinSelectedG1T(1);
                archiveList.Focus();
            };
            toolbar.Controls.Add(_previousTextureButton);
            toolbar.Controls.Add(_nextTextureButton);
            right.Controls.Add(toolbar, 0, 1);

            var imageHost = new Panel { Name = "G1TPreviewCanvas", Dock = DockStyle.Fill, BackColor = Color.FromArgb(37, 37, 37) };
            _inlinePicture = new PictureBox
            {
                Name = "G1TPreviewCanvasImage",
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = imageHost.BackColor
            };
            _inlineMessage = new Label
            {
                Name = "G1TPreviewCanvasMessage",
                Text = "Select a G1T resource",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                BackColor = imageHost.BackColor
            };
            imageHost.Controls.Add(_inlinePicture);
            imageHost.Controls.Add(_inlineMessage);
            _inlineMessage.BringToFront();
            right.Controls.Add(imageHost, 0, 2);

            _inlineDetails = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Select a texture to preview in memory. Double-click to open G1Tool.",
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            right.Controls.Add(_inlineDetails, 0, 3);

            _previewDebounce = new System.Windows.Forms.Timer { Interval = 85 };
            _previewDebounce.Tick += (_, _) =>
            {
                _previewDebounce.Stop();
                _ = LoadInlineResourceAsync();
            };

            // Preserve as much of the original list width as possible, with a usable
            // fixed-size preview on the right. The user can still drag the splitter.
            Shown += (_, _) =>
            {
                if (_previewSplit != null && _previewSplit.Width > 600)
                    _previewSplit.SplitterDistance = Math.Clamp(_previewSplit.Width - 340,
                        _previewSplit.Panel1MinSize, _previewSplit.Width - _previewSplit.Panel2MinSize - _previewSplit.SplitterWidth);
            };
            FormClosed += (_, _) => CleanupInlinePreview();
        }

        private void CleanupInlinePreview()
        {
            _inlineDisposed = true;
            ++_inlineVersion;
            _previewDebounce?.Stop();
            _previewDebounce?.Dispose();
            if (_inlinePicture != null)
            {
                Image? image = _inlinePicture.Image;
                _inlinePicture.Image = null;
                image?.Dispose();
            }
        }

        private void ShowInlineMessage(string message, string details = "")
        {
            if (_inlinePicture != null)
            {
                Image? image = _inlinePicture.Image;
                _inlinePicture.Image = null;
                image?.Dispose();
            }
            if (_inlineMessage != null)
            {
                _inlineMessage.Text = message;
                _inlineMessage.Visible = true;
                _inlineMessage.BringToFront();
            }
            if (_inlineDetails != null) _inlineDetails.Text = details;
        }

        private void ResetInlinePreview()
        {
            ++_inlineVersion;
            _previewDebounce?.Stop();
            _queuedEntry = null;
            _loadedEntry = null;
            _inlineParser = null;
            _inlineLoadFailed = false;
            _pendingTextureSteps = 0;
            _pendingLocalTextureSteps = 0;
            _selectLastOnLoad = false;
            _inlineTextureIndex = 0;
            UpdateInternalNavigationButtons();
            if (_inlineHeader != null) _inlineHeader.Text = "G1T Preview";
            ShowInlineMessage("Select a G1T resource");
        }

        private void ArchiveList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (!_internalSelectionChange) QueueSelectedResourcePreview();
        }

        private void ArchiveList_VirtualItemsSelectionRangeChanged(object? sender,
            ListViewVirtualItemsSelectionRangeChangedEventArgs e)
        {
            if (!_internalSelectionChange) QueueSelectedResourcePreview();
        }

        private void QueueSelectedResourcePreview(bool selectLast = false, bool preserveSteps = false)
        {
            if (_inlineDisposed || _previewDebounce == null) return;
            int index = archiveList.SelectedIndices.Count == 1 ? archiveList.SelectedIndices[0] : -1;
            RDBEntry? entry = index >= 0 && index < _filteredDisplayList.Count
                ? _filteredDisplayList[index] : null;

            // ListView may deliver two selection notifications for the same selection.
            if (ReferenceEquals(_queuedEntry, entry) && entry != null && !preserveSteps) return;

            ++_inlineVersion;
            _previewDebounce.Stop();
            _queuedEntry = entry;
            _loadedEntry = null;
            _inlineParser = null;
            _inlineLoadFailed = false;
            _inlineTextureIndex = 0;
            _selectLastOnLoad = selectLast;
            _pendingLocalTextureSteps = 0;
            if (!preserveSteps) _pendingTextureSteps = 0;
            UpdateInternalNavigationButtons();

            if (entry == null || _archiveExploler == null)
            {
                if (_inlineHeader != null) _inlineHeader.Text = "G1T Preview";
                ShowInlineMessage("Select a G1T resource");
                return;
            }
            if (_inlineHeader != null)
                _inlineHeader.Text = string.IsNullOrEmpty(entry.Name) ? $"0x{entry.FileKtid:X8}" : entry.Name;
            if (!IsG1TResource(entry))
            {
                ShowInlineMessage("Not a G1T resource", "Select a G1T texture file. Double-click still opens the original viewer.");
                return;
            }
            ShowInlineMessage("Loading...", "Reading G1T from archive...");
            _previewDebounce.Start();
        }

        private async Task LoadInlineResourceAsync()
        {
            RDBEntry? entry = _queuedEntry;
            ArchiveExploler? archive = _archiveExploler;
            long version = _inlineVersion;
            if (entry == null || archive == null || !IsG1TResource(entry)) return;
            bool entered = false;
            try
            {
                // Parsing, reading and decoding all share one gate with the original G1Tool.
                // A stale request is checked BEFORE entering the worker and AFTER it finishes.
                await TextureConverter.PreviewDecodeGate.WaitAsync();
                entered = true;
                if (!IsInlineRequestCurrent(version, entry)) return;
                G1TParser parsed = await Task.Run(() =>
                {
                    byte[] raw = archive.GetEntryData(entry)
                        ?? throw new IOException("Could not read this resource from the archive.");
                    if (raw.Length < 4 || raw[0] != (byte)'G' || raw[1] != (byte)'T' ||
                        raw[2] != (byte)'1' || raw[3] != (byte)'G')
                        throw new InvalidDataException("Resource does not contain a G1T header (GT1G).");
                    var parser = new G1TParser();
                    parser.Load(raw);
                    return parser;
                });
                if (!IsInlineRequestCurrent(version, entry)) return;
                _inlineParser = parsed;
                _loadedEntry = entry;
                int count = parsed.G1TFile.Textures.Count;
                if (count == 0)
                {
                    ShowInlineMessage("No textures in this G1T");
                    return;
                }
                _inlineTextureIndex = _selectLastOnLoad ? count - 1 : 0;
                // A user can press Left/Right while this file is being loaded.
                // Those presses never move to another file, even at the boundary.
                _inlineTextureIndex = Math.Clamp(_inlineTextureIndex + _pendingLocalTextureSteps, 0, count - 1);
                _pendingLocalTextureSteps = 0;
                ResolvePendingNavigation();
            }
            catch (Exception ex)
            {
                if (IsInlineRequestCurrent(version, entry))
                {
                    _inlineLoadFailed = true;
                    UpdateInternalNavigationButtons();
                    ShowInlineMessage("Preview failed", ex.Message);
                }
            }
            finally
            {
                if (entered) TextureConverter.PreviewDecodeGate.Release();
            }
        }

        private void ResolvePendingNavigation()
        {
            int count = _inlineParser?.G1TFile.Textures.Count ?? 0;
            if (count == 0) return;
            int requested = _inlineTextureIndex + (_browseTexturesCheck?.Checked == true ? _pendingTextureSteps : 0);
            _pendingTextureSteps = 0;
            if (requested >= count)
            {
                // Next G1T begins at texture 0; retain any surplus arrow presses.
                _pendingTextureSteps = requested - count;
                if (MoveToAdjacentG1T(1, false, true)) return;
                _pendingTextureSteps = 0;
                requested = count - 1;
            }
            else if (requested < 0)
            {
                // Previous G1T begins at its last texture.
                _pendingTextureSteps = requested + 1;
                if (MoveToAdjacentG1T(-1, true, true)) return;
                _pendingTextureSteps = 0;
                requested = 0;
            }
            _inlineTextureIndex = requested;
            UpdateInternalNavigationButtons();
            RequestInlineTexture();
        }

        private async void RequestInlineTexture()
        {
            RDBEntry? entry = _loadedEntry;
            G1TParser? parser = _inlineParser;
            if (entry == null || parser == null || _inlineTextureIndex < 0 ||
                _inlineTextureIndex >= parser.G1TFile.Textures.Count) return;

            UpdateInternalNavigationButtons();
            long version = ++_inlineVersion;
            int textureIndex = _inlineTextureIndex;
            G1TTexture texture = parser.G1TFile.Textures[textureIndex];
            if (texture.MipMaps.Count == 0)
            {
                ShowInlineMessage("No mipmaps in this texture");
                return;
            }
            G1TMipMap mip = texture.MipMaps[0];
            int width = (int)mip.Width;
            int height = (int)mip.Height;
            ShowInlineMessage("Loading...", $"Texture_{textureIndex:D3} / {parser.G1TFile.Textures.Count}  |  {width}x{height}  |  {texture.Format}");

            bool entered = false;
            Bitmap? bitmap = null;
            try
            {
                await TextureConverter.PreviewDecodeGate.WaitAsync();
                entered = true;
                if (!IsInlineRequestCurrent(version, entry)) return;
                bitmap = await Task.Run(() =>
                {
                    byte[]? raw = TextureConverter.DecodeG1t(texture, 0, 0);
                    return raw == null ? null : TextureConverter.CreateBitmapFromRawData(raw, width, height);
                });
                if (!IsInlineRequestCurrent(version, entry)) return;
                if (bitmap == null)
                {
                    ShowInlineMessage("Preview unavailable", $"Texture_{textureIndex:D3}: unsupported format or decoding error ({texture.Format}).");
                    return;
                }
                if (_inlinePicture != null && _inlineMessage != null)
                {
                    Image? old = _inlinePicture.Image;
                    _inlinePicture.Image = bitmap;
                    bitmap = null; // Ownership passes to the picture box.
                    old?.Dispose();
                    _inlineMessage.Visible = false;
                    if (_inlineDetails != null)
                        _inlineDetails.Text = $"Texture_{textureIndex:D3} ({textureIndex + 1}/{parser.G1TFile.Textures.Count})  |  {width}x{height}  |  {texture.Format}";
                }
            }
            catch (Exception ex)
            {
                if (IsInlineRequestCurrent(version, entry))
                    ShowInlineMessage("Preview failed", ex.Message);
            }
            finally
            {
                bitmap?.Dispose();
                if (entered) TextureConverter.PreviewDecodeGate.Release();
            }
        }

        private void UpdateInternalNavigationButtons()
        {
            int count = _inlineParser?.G1TFile.Textures.Count ?? 0;
            bool currentFileLoaded = _loadedEntry != null &&
                ReferenceEquals(_loadedEntry, _queuedEntry) && !_inlineLoadFailed;
            if (_previousTextureButton != null)
                _previousTextureButton.Enabled = currentFileLoaded && count > 1 && _inlineTextureIndex > 0;
            if (_nextTextureButton != null)
                _nextTextureButton.Enabled = currentFileLoaded && count > 1 && _inlineTextureIndex < count - 1;
        }

        // Left/Right and preview buttons NEVER change the selected resource.
        private bool NavigateWithinSelectedG1T(int direction)
        {
            int selectedIndex = archiveList.SelectedIndices.Count == 1 ? archiveList.SelectedIndices[0] : -1;
            if (selectedIndex < 0 || selectedIndex >= _filteredDisplayList.Count)
                return false;
            RDBEntry entry = _filteredDisplayList[selectedIndex];
            if (!IsG1TResource(entry)) return true; // No cross-file movement.

            if (_inlineParser != null && ReferenceEquals(_loadedEntry, entry) &&
                ReferenceEquals(_queuedEntry, entry))
            {
                int count = _inlineParser.G1TFile.Textures.Count;
                if (count <= 1) return true;
                int destination = Math.Clamp(_inlineTextureIndex + direction, 0, count - 1);
                if (destination != _inlineTextureIndex)
                {
                    _inlineTextureIndex = destination;
                    RequestInlineTexture();
                }
                return true;
            }
            if (!_inlineLoadFailed && ReferenceEquals(_queuedEntry, entry))
            {
                _pendingLocalTextureSteps += direction;
                return true;
            }
            return true;
        }

        // Up/Down: ordinary ListView navigation with both options off, otherwise
        // skip non-G1T files; Browse Textures adds sequential intra-file steps.
        private bool NavigateInlineVertical(int direction)
        {
            bool browse = _browseTexturesCheck?.Checked == true;
            bool onlyG1T = _g1tOnlyNavigationCheck?.Checked == true;
            if (!browse && !onlyG1T) return false; // Let WinForms move one normal row.
            if (_archiveExploler == null || _filteredDisplayList.Count == 0) return false;

            int current = archiveList.SelectedIndices.Count == 1 ? archiveList.SelectedIndices[0] : -1;
            if (!browse || current < 0 || current >= _filteredDisplayList.Count ||
                !IsG1TResource(_filteredDisplayList[current]))
            {
                // Direct G1T navigation always enters a NEW file at Texture_000,
                // even when going upwards.
                return MoveToAdjacentG1T(direction, selectLast: false);
            }

            RDBEntry entry = _filteredDisplayList[current];
            if (!ReferenceEquals(_queuedEntry, entry))
            {
                QueueSelectedResourcePreview();
                return true;
            }
            if (_inlineParser != null && ReferenceEquals(_loadedEntry, entry))
            {
                int count = _inlineParser.G1TFile.Textures.Count;
                if (count == 0) return MoveToAdjacentG1T(direction, selectLast: direction < 0);
                _pendingTextureSteps = direction;
                ResolvePendingNavigation();
                return true;
            }
            if (!_inlineLoadFailed)
            {
                // Keep rapid vertical keypresses while the current G1T is parsed.
                _pendingTextureSteps += direction;
                return true;
            }
            return MoveToAdjacentG1T(direction, selectLast: direction < 0);
        }

        private bool MoveToAdjacentG1T(int direction, bool selectLast, bool preserveSteps = false)
        {
            int current = archiveList.SelectedIndices.Count == 1 ? archiveList.SelectedIndices[0] :
                (direction > 0 ? -1 : _filteredDisplayList.Count);
            for (int index = current + direction; index >= 0 && index < _filteredDisplayList.Count; index += direction)
            {
                if (!IsG1TResource(_filteredDisplayList[index])) continue;
                _internalSelectionChange = true;
                try
                {
                    archiveList.SelectedIndices.Clear();
                    archiveList.Items[index].Selected = true;
                    archiveList.Items[index].Focused = true;
                    archiveList.EnsureVisible(index);
                }
                finally { _internalSelectionChange = false; }
                QueueSelectedResourcePreview(selectLast, preserveSteps);
                return true;
            }
            return false;
        }
    }
}
