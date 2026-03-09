
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PixelPalace.Core.Models;
using PixelPalace.Core.Serialization;
using PixelPalace.Core.Storage;
using PixelPalace.Gui.Controls;
using PixelPalace.Gui.Dialogs;

namespace PixelPalace.Gui;

public sealed class MainForm : Form
{
    private const int AutosaveIntervalMs = 15000;
    private const int MaxHistoryEntries = 120;

    private enum PlaybackMode
    {
        Loop,
        PingPong
    }

    private enum ReplaceScope
    {
        Layer,
        Frame,
        Document
    }

    private sealed record HistoryEntry(PixelDocument Snapshot, int FrameIndex, int LayerIndex);

    private readonly RecentProjectsStore _recentStore = new();
    private readonly AutosaveRecoveryStore _recoveryStore = new();
    private readonly PixelCanvas _canvas = new() { Dock = DockStyle.Fill };

    private readonly GradientPanel _homePanel = new()
    {
        Dock = DockStyle.Fill,
        StartColor = Color.FromArgb(25, 30, 56),
        EndColor = Color.FromArgb(17, 18, 30),
        Angle = 120f
    };

    private readonly GradientPanel _editorPanel = new()
    {
        Dock = DockStyle.Fill,
        Visible = false,
        StartColor = Color.FromArgb(17, 18, 26),
        EndColor = Color.FromArgb(22, 23, 33),
        Angle = 90f
    };

    private readonly ListView _recentList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false };
    private readonly ListView _timelineView = new() { Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = true, HideSelection = false, BackColor = Color.FromArgb(22, 24, 34), ForeColor = Color.White };
    private readonly ImageList _timelineImages = new() { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(100, 100) };

    private readonly CheckedListBox _layersList = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(23, 24, 34), ForeColor = Color.White };
    private readonly FlowLayoutPanel _palettePanel = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, BackColor = Color.FromArgb(22, 24, 34), Padding = new Padding(6) };
    private readonly FlowLayoutPanel _recentPalettePanel = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, BackColor = Color.FromArgb(20, 22, 32), Padding = new Padding(4) };
    private readonly TextBox _hexColorInput = new() { Width = 120, Text = "#000000", BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(28, 31, 46), ForeColor = Color.White };
    private readonly Panel _hexColorPreview = new() { Width = 30, Height = 30, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
    private readonly HsvColorPicker _hsvPicker = new() { Dock = DockStyle.Fill, MinimumSize = new Size(220, 160) };

    private readonly ToolStripLabel _zoomLabel = new("Zoom 20x");
    private readonly ToolStripLabel _projectLabel = new("Untitled");
    private readonly ToolStripButton _onionButton = new("Onion") { CheckOnClick = true, Checked = true };
    private readonly ToolStripButton _gridButton = new("Grid") { CheckOnClick = true, Checked = true };

    private readonly ToolStripStatusLabel _statusTool = new("Tool: Pencil");
    private readonly ToolStripStatusLabel _statusCoord = new("X: -, Y: -");
    private readonly ToolStripStatusLabel _statusFrame = new("Frame 1/1");

    private readonly NumericUpDown _fpsInput = new() { Minimum = 1, Maximum = 60, Value = 10, Width = 64 };
    private readonly NumericUpDown _frameDurationInput = new() { Minimum = 10, Maximum = 5000, Increment = 10, Value = 100, Width = 90 };
    private readonly NumericUpDown _brushSizeInput = new() { Minimum = 1, Maximum = 8, Value = 1, Width = 68 };
    private readonly NumericUpDown _playRangeStartInput = new() { Minimum = 1, Maximum = 1, Value = 1, Width = 56 };
    private readonly NumericUpDown _playRangeEndInput = new() { Minimum = 1, Maximum = 1, Value = 1, Width = 56 };
    private readonly ComboBox _playModeCombo = new() { Width = 106, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly PictureBox _previewBox = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(18, 20, 28) };
    private readonly Label _inspectorCanvasLabel = new() { Name = "CanvasSize", ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _inspectorFrameLabel = new() { Name = "FrameInfo", ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _inspectorLayerLabel = new() { Name = "LayerInfo", ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

    private readonly Label _primarySwatch = new() { Width = 36, Height = 36, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _secondarySwatch = new() { Width = 36, Height = 36, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

    private readonly CheckBox _playButton = new()
    {
        Text = "Play",
        Appearance = Appearance.Button,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(45, 49, 66),
        ForeColor = Color.White,
        Padding = new Padding(10, 4, 10, 4)
    };

    private readonly System.Windows.Forms.Timer _playbackTimer = new();
    private readonly System.Windows.Forms.Timer _autosaveTimer = new();
    private readonly Dictionary<PixelTool, Button> _toolButtons = [];
    private readonly List<HistoryEntry> _history = [];

    private PixelDocument? _document;
    private string? _currentProjectPath;
    private bool _isDirty;
    private bool _layerEventMute;
    private bool _autoFitPending;
    private bool _historyRestoring;
    private bool _autosaveNeeded;
    private bool _suppressFrameDurationEvent;
    private int _playDirection = 1;
    private int _paletteCycleIndex;
    private int _historyIndex = -1;
    private int _savedHistoryIndex = -1;
    private readonly List<Color> _recentColors = [];
    private readonly HashSet<int> _selectedFrameIndices = [];
    private bool _timelineSelectionMute;
    private bool _pickerSyncMute;

    private readonly List<Color> _palette =
    [
        Color.FromArgb(0, 0, 0), Color.FromArgb(255, 255, 255), Color.FromArgb(68, 68, 74), Color.FromArgb(117, 121, 141),
        Color.FromArgb(255, 89, 94), Color.FromArgb(255, 165, 70), Color.FromArgb(255, 220, 90), Color.FromArgb(140, 214, 84),
        Color.FromArgb(74, 196, 196), Color.FromArgb(85, 141, 255), Color.FromArgb(142, 110, 255), Color.FromArgb(219, 113, 243),
        Color.FromArgb(255, 136, 185), Color.FromArgb(255, 178, 147), Color.FromArgb(110, 72, 51), Color.FromArgb(208, 177, 128),
        Color.FromArgb(37, 21, 56), Color.FromArgb(58, 35, 86), Color.FromArgb(76, 48, 112), Color.FromArgb(109, 73, 148),
        Color.FromArgb(136, 102, 171), Color.FromArgb(170, 134, 201), Color.FromArgb(205, 168, 232), Color.FromArgb(234, 204, 250),
        Color.FromArgb(18, 38, 50), Color.FromArgb(31, 62, 82), Color.FromArgb(42, 91, 117), Color.FromArgb(52, 124, 155),
        Color.FromArgb(65, 156, 188), Color.FromArgb(91, 188, 212), Color.FromArgb(130, 219, 229), Color.FromArgb(176, 238, 242)
    ];

    public MainForm()
    {
        Text = "PixelPalace";
        Width = 1760;
        Height = 1000;
        MinimumSize = new Size(ScaleUi(1120), ScaleUi(720));
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        _timelineImages.ImageSize = new Size(ScaleUi(100), ScaleUi(100));

        _canvas.ZoomChanged += zoom => _zoomLabel.Text = $"Zoom {zoom}x";
        _canvas.HoverPixelChanged += p => _statusCoord.Text = p.X >= 0 ? $"X: {p.X}, Y: {p.Y}" : "X: -, Y: -";
        _canvas.ColorPicked += c => SetPrimaryColor(c);
        _canvas.PaintColorApplied += c => AddRecentColor(c);
        _canvas.MouseDown += (_, e) => HandleAuxMouseButtons(e.Button);
        _editorPanel.MouseDown += (_, e) => HandleAuxMouseButtons(e.Button);
        _timelineView.MouseDown += (_, e) => HandleAuxMouseButtons(e.Button);
        _palettePanel.MouseDown += (_, e) => HandleAuxMouseButtons(e.Button);
        _canvas.CanvasChanged += () =>
        {
            MarkDirty();
            RefreshTimeline();
            RefreshPreview();
        };
        _canvas.Resize += (_, _) =>
        {
            if (_autoFitPending)
            {
                _canvas.FitToView();
                _autoFitPending = false;
            }
        };

        BuildHomePanel();
        BuildEditorPanel();

        Controls.Add(_editorPanel);
        Controls.Add(_homePanel);

        _playbackTimer.Interval = 100;
        _playbackTimer.Tick += (_, _) => StepPlayback();
        _autosaveTimer.Interval = AutosaveIntervalMs;
        _autosaveTimer.Tick += (_, _) => TryWriteRecoverySnapshot();

        _playModeCombo.Items.AddRange(["Loop", "Ping-Pong"]);
        _playModeCombo.SelectedIndex = 0;
        _fpsInput.ValueChanged += (_, _) =>
        {
            if (_playbackTimer.Enabled)
            {
                _playbackTimer.Interval = Math.Max(16, 1000 / (int)_fpsInput.Value);
            }
        };
        _brushSizeInput.ValueChanged += (_, _) => _canvas.BrushSize = (int)_brushSizeInput.Value;
        _canvas.BrushSize = (int)_brushSizeInput.Value;
        _playRangeStartInput.ValueChanged += (_, _) => EnsurePlaybackRangeOrder();
        _playRangeEndInput.ValueChanged += (_, _) => EnsurePlaybackRangeOrder();
        _hexColorInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyHexInputColor();
                e.SuppressKeyPress = true;
            }
        };
        _hsvPicker.ColorChanged += color =>
        {
            if (_pickerSyncMute)
            {
                return;
            }

            SetPrimaryColor(color);
        };

        BuildPalette();
        SeedRecentColors();
        RefreshRecentPalette();
        SyncHexControlsFromPrimaryColor();
        ShowHome();
        TryPromptRecoveryOnStartup();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!_editorPanel.Visible)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        switch (keyData)
        {
            case Keys.M: SetTool(PixelTool.RectSelect); return true;
            case Keys.B: SetTool(PixelTool.Pencil); return true;
            case Keys.E: SetTool(PixelTool.Eraser); return true;
            case Keys.G: SetTool(PixelTool.Fill); return true;
            case Keys.I: SetTool(PixelTool.Picker); return true;
            case Keys.L: SetTool(PixelTool.Line); return true;
            case Keys.Control | Keys.S: SaveProject(); return true;
            case Keys.Control | Keys.A: _canvas.SelectAll(); return true;
            case Keys.Control | Keys.C: _canvas.CopySelectionToClipboard(); return true;
            case Keys.Control | Keys.X: _canvas.CutSelectionToClipboard(); return true;
            case Keys.Control | Keys.V: _canvas.PasteClipboardToSelection(); return true;
            case Keys.Delete: _canvas.DeleteSelection(); return true;
            case Keys.Escape: _canvas.ClearSelection(); return true;
            case Keys.Control | Keys.Z: Undo(); return true;
            case Keys.Control | Keys.Shift | Keys.Z: Redo(); return true;
            case Keys.Control | Keys.R: ApplySelectionMutation(() => _canvas.RotateSelection90Clockwise()); return true;
            case Keys.Control | Keys.Shift | Keys.R: ApplySelectionMutation(() => _canvas.RotateSelection90CounterClockwise()); return true;
            case Keys.Control | Keys.Shift | Keys.S: SaveProjectAs(); return true;
            case Keys.Control | Keys.N: CreateNewProject(); return true;
            case Keys.Control | Keys.O: OpenProjectViaDialog(); return true;
            case Keys.Control | Keys.Alt | Keys.Left:
                MoveSelectedFrames(-1);
                return true;
            case Keys.Control | Keys.Alt | Keys.Right:
                MoveSelectedFrames(1);
                return true;
            case Keys.Add:
            case Keys.Oemplus:
                _canvas.StepZoom(1, new Point(_canvas.Width / 2, _canvas.Height / 2));
                return true;
            case Keys.Subtract:
            case Keys.OemMinus:
                _canvas.StepZoom(-1, new Point(_canvas.Width / 2, _canvas.Height / 2));
                return true;
            case Keys.Space:
                _canvas.SpacePanning = true;
                return true;
            case Keys.Left:
                if (_canvas.NudgeSelection(-1, 0))
                {
                    return true;
                }

                SelectFrame(Math.Max(0, _canvas.CurrentFrameIndex - 1));
                return true;
            case Keys.Right:
                if (_canvas.NudgeSelection(1, 0))
                {
                    return true;
                }

                SelectFrame(Math.Min((_document?.Frames.Count ?? 1) - 1, _canvas.CurrentFrameIndex + 1));
                return true;
            case Keys.Shift | Keys.Left:
                if (_canvas.NudgeSelection(-10, 0))
                {
                    return true;
                }

                return true;
            case Keys.Shift | Keys.Right:
                if (_canvas.NudgeSelection(10, 0))
                {
                    return true;
                }

                return true;
            case Keys.Up:
                if (_canvas.NudgeSelection(0, -1))
                {
                    return true;
                }

                break;
            case Keys.Down:
                if (_canvas.NudgeSelection(0, 1))
                {
                    return true;
                }

                break;
            case Keys.Shift | Keys.Up:
                if (_canvas.NudgeSelection(0, -10))
                {
                    return true;
                }

                return true;
            case Keys.Shift | Keys.Down:
                if (_canvas.NudgeSelection(0, 10))
                {
                    return true;
                }

                return true;
            case Keys.OemOpenBrackets:
                _brushSizeInput.Value = Math.Max(_brushSizeInput.Minimum, _brushSizeInput.Value - 1);
                return true;
            case Keys.OemCloseBrackets:
                _brushSizeInput.Value = Math.Min(_brushSizeInput.Maximum, _brushSizeInput.Value + 1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private int ScaleUi(int px) => (int)Math.Ceiling(px * DeviceDpi / 96f);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space)
        {
            _canvas.SpacePanning = false;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        HandleAuxMouseButtons(e.Button);
    }

    private void BuildHomePanel()
    {
        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            AutoScroll = true,
            Padding = new Padding(46, 38, 46, 26),
            BackColor = Color.FromArgb(24, 27, 42)
        };
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var title = new Label
        {
            Text = "PIXELPALACE",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 38, FontStyle.Bold),
            ForeColor = Color.FromArgb(241, 244, 255),
            TextAlign = ContentAlignment.BottomLeft
        };
        var subtitle = new Label
        {
            Text = "Playful pixel animation studio. Build sprites, animate fast, ship faster.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 12, FontStyle.Regular),
            ForeColor = Color.FromArgb(178, 188, 228),
            TextAlign = ContentAlignment.TopLeft
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        var newBtn = new Button
        {
            Text = "New Project",
            Width = 168,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(96, 145, 255),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        newBtn.FlatAppearance.BorderSize = 0;
        newBtn.Click += (_, _) => CreateNewProject();

        var openBtn = new Button
        {
            Text = "Open Project",
            Width = 168,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(83, 90, 133),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        openBtn.FlatAppearance.BorderSize = 0;
        openBtn.Click += (_, _) => OpenProjectViaDialog();
        actions.Controls.Add(newBtn);
        actions.Controls.Add(openBtn);

        var recentsCard = new NeonCard { Dock = DockStyle.Fill, BorderColor = Color.FromArgb(107, 150, 255) };
        var recentsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        recentsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        recentsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        recentsLayout.Controls.Add(new Label
        {
            Text = "Recent Projects",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(229, 235, 255),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _recentList.Columns.Clear();
        _recentList.Columns.Add("Name", 220);
        _recentList.Columns.Add("Location", 560);
        _recentList.Columns.Add("Last Opened", 170);
        _recentList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _recentList.BackColor = Color.FromArgb(17, 19, 29);
        _recentList.ForeColor = Color.FromArgb(218, 224, 245);
        _recentList.BorderStyle = BorderStyle.None;
        _recentList.DoubleClick += (_, _) =>
        {
            if (_recentList.SelectedItems.Count == 0)
            {
                return;
            }

            var path = _recentList.SelectedItems[0].Tag as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                OpenProject(path);
            }
        };

        recentsLayout.Controls.Add(_recentList, 0, 1);
        recentsCard.Controls.Add(recentsLayout);

        wrap.Controls.Add(title, 1, 0);
        wrap.Controls.Add(subtitle, 1, 1);
        wrap.Controls.Add(actions, 1, 2);
        wrap.Controls.Add(recentsCard, 1, 3);
        wrap.Controls.Add(new Label
        {
            Text = "Shortcuts: B/E/G/I/L tools, Ctrl+S save, Space pan, Mouse wheel zoom",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(136, 145, 184),
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 4);

        _homePanel.Controls.Add(wrap);
    }
    private void BuildEditorPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(30)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(40)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(26)));

        root.Controls.Add(BuildMenuStrip(), 0, 0);
        root.Controls.Add(BuildToolStrip(), 0, 1);
        root.Controls.Add(BuildWorkspace(), 0, 2);
        root.Controls.Add(BuildStatusStrip(), 0, 3);

        _editorPanel.Controls.Add(root);
    }

    private MenuStrip BuildMenuStrip()
    {
        var menu = new MenuStrip { Dock = DockStyle.Fill, BackColor = Color.FromArgb(17, 19, 28), ForeColor = Color.White };

        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add("Home", null, (_, _) => GoHome());
        file.DropDownItems.Add("New", null, (_, _) => CreateNewProject());
        file.DropDownItems.Add("Open", null, (_, _) => OpenProjectViaDialog());
        file.DropDownItems.Add("Save", null, (_, _) => SaveProject());
        file.DropDownItems.Add("Save As", null, (_, _) => SaveProjectAs());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Export Frame PNG", null, (_, _) => ExportFramePng());
        file.DropDownItems.Add("Export Sprite Sheet PNG", null, (_, _) => ExportSpriteSheet());

        var view = new ToolStripMenuItem("View");
        view.DropDownItems.Add("Fit Canvas", null, (_, _) => _canvas.FitToView());
        view.DropDownItems.Add("Center Canvas", null, (_, _) => _canvas.CenterCanvas());

        var edit = new ToolStripMenuItem("Edit");
        edit.DropDownItems.Add("Undo", null, (_, _) => Undo());
        edit.DropDownItems.Add("Redo", null, (_, _) => Redo());
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Copy Selection", null, (_, _) => _canvas.CopySelectionToClipboard());
        edit.DropDownItems.Add("Cut Selection", null, (_, _) =>
        {
            _canvas.CutSelectionToClipboard();
        });
        edit.DropDownItems.Add("Paste", null, (_, _) =>
        {
            _canvas.PasteClipboardToSelection();
        });
        edit.DropDownItems.Add("Select All", null, (_, _) => _canvas.SelectAll());
        edit.DropDownItems.Add("Delete Selection", null, (_, _) => _canvas.DeleteSelection());
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Flip Selection Horizontal", null, (_, _) => ApplySelectionMutation(() => _canvas.FlipSelectionHorizontal()));
        edit.DropDownItems.Add("Flip Selection Vertical", null, (_, _) => ApplySelectionMutation(() => _canvas.FlipSelectionVertical()));
        edit.DropDownItems.Add("Rotate Selection 90 CW", null, (_, _) => ApplySelectionMutation(() => _canvas.RotateSelection90Clockwise()));
        edit.DropDownItems.Add("Rotate Selection 90 CCW", null, (_, _) => ApplySelectionMutation(() => _canvas.RotateSelection90CounterClockwise()));
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Replace Primary -> Secondary (Layer)", null, (_, _) => ReplacePrimaryWithSecondary(ReplaceScope.Layer));
        edit.DropDownItems.Add("Replace Primary -> Secondary (Frame)", null, (_, _) => ReplacePrimaryWithSecondary(ReplaceScope.Frame));
        edit.DropDownItems.Add("Replace Primary -> Secondary (Document)", null, (_, _) => ReplacePrimaryWithSecondary(ReplaceScope.Document));

        var anim = new ToolStripMenuItem("Animation");
        anim.DropDownItems.Add("Play/Stop", null, (_, _) => TogglePlayback());
        anim.DropDownItems.Add("Duplicate Frame", null, (_, _) => DuplicateFrame());

        menu.Items.Add(file);
        menu.Items.Add(edit);
        menu.Items.Add(view);
        menu.Items.Add(anim);
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        var bar = new ToolStrip { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.FromArgb(24, 27, 39), ForeColor = Color.White };

        bar.Items.Add(new ToolStripButton("Home", null, (_, _) => GoHome()));
        bar.Items.Add(new ToolStripButton("New", null, (_, _) => CreateNewProject()));
        bar.Items.Add(new ToolStripButton("Open", null, (_, _) => OpenProjectViaDialog()));
        bar.Items.Add(new ToolStripButton("Save", null, (_, _) => SaveProject()));
        bar.Items.Add(new ToolStripButton("Undo", null, (_, _) => Undo()));
        bar.Items.Add(new ToolStripButton("Redo", null, (_, _) => Redo()));
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(new ToolStripButton("Fit", null, (_, _) => _canvas.FitToView()));
        bar.Items.Add(new ToolStripButton("Zoom +", null, (_, _) => _canvas.StepZoom(1, new Point(_canvas.Width / 2, _canvas.Height / 2))));
        bar.Items.Add(new ToolStripButton("Zoom -", null, (_, _) => _canvas.StepZoom(-1, new Point(_canvas.Width / 2, _canvas.Height / 2))));

        _gridButton.CheckedChanged += (_, _) => { _canvas.ShowGrid = _gridButton.Checked; _canvas.Invalidate(); };
        _onionButton.CheckedChanged += (_, _) => { _canvas.ShowOnionSkin = _onionButton.Checked; _canvas.Invalidate(); };

        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(_gridButton);
        bar.Items.Add(_onionButton);
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(_zoomLabel);
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(_projectLabel);

        return bar;
    }

    private Control BuildWorkspace()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(230)));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleUi(360)));

        root.Controls.Add(BuildToolRail(), 0, 0);
        root.Controls.Add(BuildCenterArea(), 1, 0);
        root.Controls.Add(BuildRightArea(), 2, 0);

        return root;
    }

    private Control BuildToolRail()
    {
        var panel = new NeonCard { Dock = DockStyle.Fill, BorderColor = Color.FromArgb(116, 157, 255), Margin = new Padding(10) };
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 11
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 11; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        layout.Controls.Add(CreateToolButton("Pencil B", PixelTool.Pencil, Color.FromArgb(103, 145, 255)), 0, 0);
        layout.Controls.Add(CreateToolButton("Select M", PixelTool.RectSelect, Color.FromArgb(90, 189, 170)), 0, 1);
        layout.Controls.Add(CreateToolButton("Eraser E", PixelTool.Eraser, Color.FromArgb(255, 119, 119)), 0, 2);
        layout.Controls.Add(CreateToolButton("Fill G", PixelTool.Fill, Color.FromArgb(255, 166, 71)), 0, 3);
        layout.Controls.Add(CreateToolButton("Picker I", PixelTool.Picker, Color.FromArgb(122, 194, 126)), 0, 4);
        layout.Controls.Add(CreateToolButton("Line L", PixelTool.Line, Color.FromArgb(189, 128, 255)), 0, 5);

        layout.Controls.Add(new Label { Text = "Brush [ / ]", Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        var brushRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
        brushRow.Controls.Add(_brushSizeInput);
        brushRow.Controls.Add(new Label { Text = "px", ForeColor = Color.White, AutoSize = true, Margin = new Padding(2, 8, 0, 0) });
        layout.Controls.Add(brushRow, 0, 7);

        layout.Controls.Add(new Label { Text = "Primary / Secondary", Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft }, 0, 9);

        var swatchRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true };
        _primarySwatch.Click += (_, _) => PickCustomColor(true);
        _secondarySwatch.Click += (_, _) => PickCustomColor(false);
        var swap = new CandyButton { Text = "Swap", AccentColor = Color.FromArgb(86, 92, 136) };
        swap.Click += (_, _) => SwapColors();
        swatchRow.Controls.Add(_primarySwatch);
        swatchRow.Controls.Add(_secondarySwatch);
        swatchRow.Controls.Add(swap);
        layout.Controls.Add(swatchRow, 0, 10);

        scroller.Controls.Add(layout);
        panel.Controls.Add(scroller);
        return panel;
    }

    private Control BuildCenterArea()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(0, 10, 0, 10) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(300)));

        var canvasCard = new NeonCard { Dock = DockStyle.Fill, BorderColor = Color.FromArgb(102, 164, 255), Margin = new Padding(0, 0, 10, 10) };
        canvasCard.Controls.Add(_canvas);

        var timelineCard = new NeonCard
        {
            Dock = DockStyle.Fill,
            BorderColor = Color.FromArgb(129, 126, 255),
            Margin = new Padding(0, 0, 10, 0),
            MinimumSize = new Size(0, ScaleUi(250))
        };
        var timelineLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 4, 4, 2),
            AutoSize = true
        };
        top.Controls.Add(CreateMiniButton("+ Frame", (_, _) => AddFrame()));
        top.Controls.Add(CreateMiniButton("Duplicate", (_, _) => DuplicateFrame()));
        top.Controls.Add(CreateMiniButton("- Frame", (_, _) => DeleteFrame()));
        top.Controls.Add(CreateMiniButton("Duplicate Sel", (_, _) => DuplicateSelectedFrames()));
        top.Controls.Add(CreateMiniButton("Delete Sel", (_, _) => DeleteSelectedFrames()));
        top.Controls.Add(CreateMiniButton("Move Sel <", (_, _) => MoveSelectedFrames(-1)));
        top.Controls.Add(CreateMiniButton("Move Sel >", (_, _) => MoveSelectedFrames(1)));
        top.Controls.Add(CreateMiniButton("<", (_, _) => SelectFrame(Math.Max(0, _canvas.CurrentFrameIndex - 1))));
        top.Controls.Add(CreateMiniButton(">", (_, _) => SelectFrame(Math.Min((_document?.Frames.Count ?? 1) - 1, _canvas.CurrentFrameIndex + 1))));
        _playButton.CheckedChanged += (_, _) => TogglePlayback();
        top.Controls.Add(_playButton);
        top.Controls.Add(new Label { Text = "Mode", ForeColor = Color.White, Width = 42, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(8, 8, 3, 0) });
        top.Controls.Add(_playModeCombo);
        top.Controls.Add(new Label { Text = "FPS", ForeColor = Color.White, Width = 30, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(10, 8, 3, 0) });
        top.Controls.Add(_fpsInput);
        top.Controls.Add(new Label { Text = "Duration", ForeColor = Color.White, Width = 60, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(10, 8, 3, 0) });
        _frameDurationInput.ValueChanged += (_, _) =>
        {
            if (_document is null || _suppressFrameDurationEvent)
            {
                return;
            }

            _document.Frames[_canvas.CurrentFrameIndex].DurationMs = (int)_frameDurationInput.Value;
            MarkDirty();
            RefreshTimeline();
        };
        top.Controls.Add(_frameDurationInput);
        top.Controls.Add(new Label { Text = "Start", ForeColor = Color.White, Width = 40, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(10, 8, 3, 0) });
        top.Controls.Add(_playRangeStartInput);
        top.Controls.Add(new Label { Text = "End", ForeColor = Color.White, Width = 32, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(8, 8, 3, 0) });
        top.Controls.Add(_playRangeEndInput);

        _timelineView.LargeImageList = _timelineImages;
        _timelineView.AllowDrop = true;
        _timelineView.SelectedIndexChanged += (_, _) =>
        {
            if (_timelineSelectionMute || _document is null)
            {
                return;
            }

            if (_timelineView.SelectedIndices.Count <= 0)
            {
                return;
            }

            _selectedFrameIndices.Clear();
            foreach (int selected in _timelineView.SelectedIndices)
            {
                _selectedFrameIndices.Add(selected);
            }

            var nextCurrent = _timelineView.FocusedItem?.Index ?? _timelineView.SelectedIndices[0];
            if (nextCurrent != _canvas.CurrentFrameIndex && nextCurrent >= 0 && nextCurrent < _document.Frames.Count)
            {
                _canvas.CurrentFrameIndex = nextCurrent;
                _canvas.CurrentLayerIndex = Math.Clamp(_canvas.CurrentLayerIndex, 0, _document.Frames[nextCurrent].Layers.Count - 1);
                _canvas.ClearSelection();
                RefreshLayers();
                RefreshInspector();
                RefreshPreview();
                RefreshStatus();
                _canvas.Invalidate();
            }
        };
        _timelineView.ItemDrag += (_, e) =>
        {
            if (_document is null || _timelineView.SelectedIndices.Count == 0)
            {
                return;
            }

            var dragged = _timelineView.SelectedIndices.Cast<int>().OrderBy(i => i).ToArray();
            _timelineView.DoDragDrop(dragged, DragDropEffects.Move);
        };
        _timelineView.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(typeof(int[])) == true)
            {
                e.Effect = DragDropEffects.Move;
            }
        };
        _timelineView.DragOver += (_, e) =>
        {
            e.Effect = e.Data?.GetDataPresent(typeof(int[])) == true ? DragDropEffects.Move : DragDropEffects.None;
        };
        _timelineView.DragDrop += (_, e) =>
        {
            if (_document is null || e.Data?.GetData(typeof(int[])) is not int[] dragged || dragged.Length == 0)
            {
                return;
            }

            var point = _timelineView.PointToClient(new Point(e.X, e.Y));
            var targetItem = _timelineView.GetItemAt(point.X, point.Y);
            var targetIndex = targetItem?.Index ?? (_document.Frames.Count - 1);
            ReorderFrames(dragged, targetIndex);
        };

        timelineLayout.Controls.Add(top, 0, 0);
        timelineLayout.Controls.Add(_timelineView, 0, 1);
        timelineCard.Controls.Add(timelineLayout);

        root.Controls.Add(canvasCard, 0, 0);
        root.Controls.Add(timelineCard, 0, 1);

        return root;
    }

    private Control BuildRightArea()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 10, 10) };

        var paletteTab = new TabPage("Palette") { BackColor = Color.FromArgb(24, 26, 36) };
        var paletteLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, Padding = new Padding(6) };
        paletteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(24)));
        paletteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(74)));
        paletteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(58)));
        paletteLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        paletteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(24)));
        paletteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(220)));

        paletteLayout.Controls.Add(new Label { Text = "Recent Colors", Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        paletteLayout.Controls.Add(_recentPalettePanel, 0, 1);

        var paletteActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 2, 0, 2)
        };
        paletteActions.Controls.Add(CreateMiniButton("Add Current", (_, _) => AddCurrentColorToPalette()));
        paletteActions.Controls.Add(CreateMiniButton("Sort Hue", (_, _) => SortPaletteByHue()));
        paletteLayout.Controls.Add(paletteActions, 0, 2);

        paletteLayout.Controls.Add(_palettePanel, 0, 3);
        paletteLayout.Controls.Add(new Label
        {
            Text = "Color Grid",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 4);

        var chooserCard = new NeonCard { Dock = DockStyle.Fill, BorderColor = Color.FromArgb(95, 171, 255), Padding = new Padding(8) };
        var chooserLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        chooserLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        chooserLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(40)));
        chooserLayout.Controls.Add(_hsvPicker, 0, 0);

        var chooserRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 2, 0, 0)
        };
        chooserRow.Controls.Add(_hexColorPreview);
        chooserRow.Controls.Add(_hexColorInput);
        chooserRow.Controls.Add(CreateMiniButton("Apply Hex", (_, _) => ApplyHexInputColor()));
        chooserRow.Controls.Add(CreateMiniButton("Pick...", (_, _) =>
        {
            using var dialog = new ColorDialog { Color = _canvas.PrimaryColor };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                SetPrimaryColor(dialog.Color);
            }
        }));
        chooserLayout.Controls.Add(chooserRow, 0, 1);

        chooserCard.Controls.Add(chooserLayout);
        paletteLayout.Controls.Add(chooserCard, 0, 5);
        paletteTab.Controls.Add(paletteLayout);

        var layersTab = new TabPage("Layers") { BackColor = Color.FromArgb(24, 26, 36) };
        var layersLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        layersLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layersLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(42)));

        _layersList.CheckOnClick = true;
        _layersList.SelectedIndexChanged += (_, _) =>
        {
            if (_document is null || _layerEventMute || _layersList.SelectedIndex < 0)
            {
                return;
            }

            _canvas.CurrentLayerIndex = ToLayerModelIndex(_layersList.SelectedIndex);
            RefreshStatus();
        };

        _layersList.ItemCheck += (_, e) =>
        {
            if (_document is null || _layerEventMute)
            {
                return;
            }

            var layerIndex = ToLayerModelIndex(e.Index);
            _document.Frames[_canvas.CurrentFrameIndex].Layers[layerIndex].Visible = e.NewValue == CheckState.Checked;
            MarkDirty();
            _canvas.Invalidate();
            BeginInvoke(() => RefreshLayers());
        };

        var layerBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        layerBtns.Controls.Add(CreateMiniButton("+ Layer", (_, _) => AddLayer()));
        layerBtns.Controls.Add(CreateMiniButton("Duplicate", (_, _) => DuplicateLayer()));
        layerBtns.Controls.Add(CreateMiniButton("- Layer", (_, _) => DeleteLayer()));
        layerBtns.Controls.Add(CreateMiniButton("Up", (_, _) => MoveLayer(1)));
        layerBtns.Controls.Add(CreateMiniButton("Down", (_, _) => MoveLayer(-1)));

        layersLayout.Controls.Add(_layersList, 0, 0);
        layersLayout.Controls.Add(layerBtns, 0, 1);
        layersTab.Controls.Add(layersLayout);

        var inspectorTab = new TabPage("Inspector") { BackColor = Color.FromArgb(24, 26, 36) };
        var inspectorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, Padding = new Padding(8) };
        inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(24)));
        inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(24)));
        inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(24)));
        inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(24)));
        inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(10)));
        inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inspectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleUi(140)));

        inspectorLayout.Controls.Add(_inspectorCanvasLabel, 0, 0);
        inspectorLayout.Controls.Add(_inspectorFrameLabel, 0, 1);
        inspectorLayout.Controls.Add(_inspectorLayerLabel, 0, 2);
        inspectorLayout.Controls.Add(new Label { Text = "Current Frame Preview", ForeColor = Color.White, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 3);
        inspectorLayout.Controls.Add(_previewBox, 0, 6);

        inspectorTab.Controls.Add(inspectorLayout);

        tabs.Controls.Add(paletteTab);
        tabs.Controls.Add(layersTab);
        tabs.Controls.Add(inspectorTab);

        return tabs;
    }

    private StatusStrip BuildStatusStrip()
    {
        var bar = new StatusStrip { Dock = DockStyle.Fill, BackColor = Color.FromArgb(17, 18, 26), ForeColor = Color.White };
        bar.Items.Add(_statusTool);
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(_statusCoord);
        bar.Items.Add(new ToolStripSeparator());
        bar.Items.Add(_statusFrame);
        return bar;
    }

    private Button CreateToolButton(string text, PixelTool tool, Color accent)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent,
            ForeColor = Color.White,
            AutoSize = false,
            MinimumSize = new Size(ScaleUi(160), ScaleUi(44)),
            Margin = new Padding(4),
            Padding = new Padding(10, 2, 10, 2),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => SetTool(tool);
        _toolButtons[tool] = button;
        if (tool == PixelTool.Pencil)
        {
            HighlightTool(tool);
        }

        return button;
    }

    private Button CreateMiniButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 4, 10, 4),
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(43, 48, 70),
            ForeColor = Color.White
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += onClick;
        return b;
    }

    private void BuildPalette()
    {
        _paletteCycleIndex = Math.Clamp(_paletteCycleIndex, 0, Math.Max(0, _palette.Count - 1));
        _palettePanel.Controls.Clear();

        foreach (var color in _palette)
        {
            var swatch = new Panel
            {
                BackColor = color,
                Width = 28,
                Height = 28,
                Margin = new Padding(3),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };

            swatch.Click += (_, _) => SetPrimaryColor(color);
            swatch.MouseDown += (_, e) => HandleAuxMouseButtons(e.Button);
            _palettePanel.Controls.Add(swatch);
        }
    }

    private void SeedRecentColors()
    {
        _recentColors.Clear();
        foreach (var color in _palette.Take(8))
        {
            _recentColors.Add(color);
        }
    }

    private void AddRecentColor(Color color)
    {
        _recentColors.RemoveAll(c => c.ToArgb() == color.ToArgb());
        _recentColors.Insert(0, color);
        if (_recentColors.Count > 18)
        {
            _recentColors.RemoveRange(18, _recentColors.Count - 18);
        }

        RefreshRecentPalette();
    }

    private void RefreshRecentPalette()
    {
        _recentPalettePanel.Controls.Clear();
        foreach (var color in _recentColors)
        {
            var swatch = new Panel
            {
                BackColor = color,
                Width = 24,
                Height = 24,
                Margin = new Padding(2),
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            swatch.Click += (_, _) => SetPrimaryColor(color);
            swatch.MouseDown += (_, e) => HandleAuxMouseButtons(e.Button);
            _recentPalettePanel.Controls.Add(swatch);
        }
    }

    private void AddCurrentColorToPalette()
    {
        var current = _canvas.PrimaryColor;
        if (_palette.Any(c => c.ToArgb() == current.ToArgb()))
        {
            return;
        }

        _palette.Add(current);
        BuildPalette();
    }

    private void SortPaletteByHue()
    {
        _palette.Sort((a, b) =>
        {
            var cmp = a.GetHue().CompareTo(b.GetHue());
            if (cmp != 0)
            {
                return cmp;
            }

            return a.GetBrightness().CompareTo(b.GetBrightness());
        });
        BuildPalette();
    }

    private void HandleAuxMouseButtons(MouseButtons button)
    {
        if (button == MouseButtons.XButton1)
        {
            SwapColors();
        }
        else if (button == MouseButtons.XButton2)
        {
            CyclePaletteForward();
        }
    }

    private void CyclePaletteForward()
    {
        if (_palette.Count == 0)
        {
            return;
        }

        _paletteCycleIndex = (_paletteCycleIndex + 1) % _palette.Count;
        SetPrimaryColor(_palette[_paletteCycleIndex]);
    }
    private void ShowHome()
    {
        _homePanel.Visible = true;
        _editorPanel.Visible = false;
        _autosaveTimer.Stop();
        RefreshRecents();
    }

    private void ShowEditor()
    {
        _homePanel.Visible = false;
        _editorPanel.Visible = true;
        _autosaveTimer.Start();
        _canvas.Focus();
    }

    private void GoHome()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        StopPlayback();
        ShowHome();
    }

    private void RefreshRecents()
    {
        var recents = _recentStore.Load();
        _recentList.Items.Clear();

        foreach (var recent in recents)
        {
            if (!File.Exists(recent.Path))
            {
                continue;
            }

            var item = new ListViewItem(Path.GetFileNameWithoutExtension(recent.Path));
            item.SubItems.Add(recent.Path);
            item.SubItems.Add(recent.LastOpenedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            item.Tag = recent.Path;
            _recentList.Items.Add(item);
        }
    }

    private void CreateNewProject()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        using var dialog = new NewDocumentDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _document = new PixelDocument(dialog.CanvasWidth, dialog.CanvasHeight);
        _currentProjectPath = null;
        _isDirty = false;
        _autosaveNeeded = false;

        BindDocument();
        ShowEditor();
    }

    private void OpenProjectViaDialog()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "PixelPalace Project|*.ppal|JSON|*.json|All files|*.*"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            OpenProject(dialog.FileName);
        }
    }

    private void OpenProject(string path)
    {
        try
        {
            _document = PixelProjectSerializer.Load(path);
            _currentProjectPath = path;
            _isDirty = false;
            _autosaveNeeded = false;
            _recentStore.Touch(path);

            BindDocument();
            ShowEditor();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to open project:\n{ex.Message}", "Open Project", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BindDocument()
    {
        if (_document is null)
        {
            return;
        }

        _canvas.Document = _document;
        _canvas.CurrentFrameIndex = 0;
        _canvas.CurrentLayerIndex = 0;
        _autoFitPending = true;
        _canvas.FitToView();
        _autoFitPending = false;

        SetPrimaryColor(Color.Black);
        SetSecondaryColor(Color.White);
        SetTool(PixelTool.Pencil);
        ResetHistory();

        RefreshAllViews();
    }

    private void RefreshAllViews()
    {
        RefreshWindowTitle();
        RefreshTimeline();
        RefreshLayers();
        RefreshInspector();
        RefreshPreview();
        RefreshStatus();
        _canvas.Invalidate();
    }

    private void SetTool(PixelTool tool)
    {
        _canvas.Tool = tool;
        HighlightTool(tool);
        RefreshStatus();
    }

    private void HighlightTool(PixelTool selected)
    {
        foreach (var (tool, button) in _toolButtons)
        {
            button.BackColor = tool == selected ? Color.FromArgb(97, 151, 255) : Color.FromArgb(85, 95, 130);
        }
    }

    private void SetPrimaryColor(Color color)
    {
        _canvas.PrimaryColor = color;
        _primarySwatch.BackColor = color;
        SyncHexControlsFromPrimaryColor();
    }

    private void SetSecondaryColor(Color color)
    {
        _canvas.SecondaryColor = color;
        _secondarySwatch.BackColor = color;
    }

    private void SwapColors()
    {
        var temp = _canvas.PrimaryColor;
        SetPrimaryColor(_canvas.SecondaryColor);
        SetSecondaryColor(temp);
    }

    private void PickCustomColor(bool primary)
    {
        using var dialog = new ColorDialog { Color = primary ? _canvas.PrimaryColor : _canvas.SecondaryColor };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (primary)
        {
            SetPrimaryColor(dialog.Color);
        }
        else
        {
            SetSecondaryColor(dialog.Color);
        }
    }

    private void RefreshTimeline()
    {
        if (_document is null)
        {
            return;
        }

        _timelineSelectionMute = true;
        _timelineImages.Images.Clear();
        _timelineView.Items.Clear();

        for (var i = 0; i < _document.Frames.Count; i++)
        {
            using var bitmap = _document.RenderFrameBitmap(i);
            _timelineImages.Images.Add(BuildFrameThumbnail(bitmap, _timelineImages.ImageSize));
            _timelineView.Items.Add(new ListViewItem($"F{i + 1} ({_document.Frames[i].DurationMs}ms)", i));
        }

        if (_document.Frames.Count > 0)
        {
            var idx = Math.Clamp(_canvas.CurrentFrameIndex, 0, _document.Frames.Count - 1);
            _selectedFrameIndices.RemoveWhere(i => i < 0 || i >= _document.Frames.Count);
            if (_selectedFrameIndices.Count == 0)
            {
                _selectedFrameIndices.Add(idx);
            }

            foreach (var selected in _selectedFrameIndices)
            {
                _timelineView.Items[selected].Selected = true;
            }

            _timelineView.Items[idx].Focused = true;
            _timelineView.EnsureVisible(idx);
            _suppressFrameDurationEvent = true;
            try
            {
                _frameDurationInput.Value = Math.Clamp(_document.Frames[idx].DurationMs, (int)_frameDurationInput.Minimum, (int)_frameDurationInput.Maximum);
            }
            finally
            {
                _suppressFrameDurationEvent = false;
            }
        }
        _timelineSelectionMute = false;

        UpdatePlaybackRangeInputs();

        RefreshStatus();
    }

    private static Bitmap BuildFrameThumbnail(Bitmap source, Size size)
    {
        var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(19, 22, 32));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var scale = Math.Min((size.Width - 12f) / source.Width, (size.Height - 12f) / source.Height);
        var width = source.Width * Math.Max(1f, scale);
        var height = source.Height * Math.Max(1f, scale);
        var x = (size.Width - width) / 2f;
        var y = (size.Height - height) / 2f;

        g.DrawImage(source, x, y, width, height);
        using var border = new Pen(Color.FromArgb(112, 172, 255));
        g.DrawRectangle(border, x, y, width, height);

        return bmp;
    }

    private void RefreshLayers()
    {
        if (_document is null)
        {
            return;
        }

        _layerEventMute = true;
        try
        {
            _layersList.Items.Clear();
            var frame = _document.Frames[_canvas.CurrentFrameIndex];

            for (var visual = 0; visual < frame.Layers.Count; visual++)
            {
                var model = frame.Layers.Count - 1 - visual;
                var layer = frame.Layers[model];
                _layersList.Items.Add(layer.Name, layer.Visible);
            }

            var selectedVisual = frame.Layers.Count - 1 - _canvas.CurrentLayerIndex;
            if (selectedVisual >= 0 && selectedVisual < _layersList.Items.Count)
            {
                _layersList.SelectedIndex = selectedVisual;
            }
        }
        finally
        {
            _layerEventMute = false;
        }
    }

    private void RefreshInspector()
    {
        if (_document is null)
        {
            return;
        }

        _inspectorCanvasLabel.Text = $"Canvas: {_document.Width} x {_document.Height}";
        _inspectorFrameLabel.Text = $"Frame: {_canvas.CurrentFrameIndex + 1}/{_document.Frames.Count}";
        _inspectorLayerLabel.Text = $"Layer: {_canvas.CurrentLayerIndex + 1}";
    }

    private void RefreshPreview()
    {
        if (_document is null)
        {
            _previewBox.Image = null;
            return;
        }

        _previewBox.Image?.Dispose();
        _previewBox.Image = _document.RenderFrameBitmap(_canvas.CurrentFrameIndex);
    }

    private int ToLayerModelIndex(int visualIndex)
    {
        if (_document is null)
        {
            return 0;
        }

        return _document.Frames[_canvas.CurrentFrameIndex].Layers.Count - 1 - visualIndex;
    }

    private void SelectFrame(int index)
    {
        if (_document is null || index < 0 || index >= _document.Frames.Count)
        {
            return;
        }

        _canvas.CurrentFrameIndex = index;
        _selectedFrameIndices.Clear();
        _selectedFrameIndices.Add(index);
        _canvas.CurrentLayerIndex = Math.Clamp(_canvas.CurrentLayerIndex, 0, _document.Frames[index].Layers.Count - 1);
        _canvas.ClearSelection();
        RefreshTimeline();
        RefreshLayers();
        RefreshInspector();
        RefreshPreview();
        RefreshStatus();
        _canvas.Invalidate();
    }
    private void AddFrame()
    {
        if (_document is null)
        {
            return;
        }

        var frame = new PixelFrame($"Frame {_document.Frames.Count + 1}");
        frame.Layers.Add(new PixelLayer(_document.Width, _document.Height, "Layer 1"));
        _document.Frames.Add(frame);
        _canvas.CurrentFrameIndex = _document.Frames.Count - 1;
        _selectedFrameIndices.Clear();
        _selectedFrameIndices.Add(_canvas.CurrentFrameIndex);
        _canvas.CurrentLayerIndex = 0;

        MarkDirty();
        RefreshAllViews();
    }

    private void DuplicateFrame()
    {
        if (_document is null)
        {
            return;
        }

        var clone = _document.Frames[_canvas.CurrentFrameIndex].Clone($"Frame {_document.Frames.Count + 1}");
        _document.Frames.Insert(_canvas.CurrentFrameIndex + 1, clone);
        _canvas.CurrentFrameIndex++;
        _selectedFrameIndices.Clear();
        _selectedFrameIndices.Add(_canvas.CurrentFrameIndex);

        MarkDirty();
        RefreshAllViews();
    }

    private void DeleteFrame()
    {
        if (_document is null || _document.Frames.Count <= 1)
        {
            return;
        }

        _document.Frames.RemoveAt(_canvas.CurrentFrameIndex);
        _canvas.CurrentFrameIndex = Math.Clamp(_canvas.CurrentFrameIndex, 0, _document.Frames.Count - 1);
        _selectedFrameIndices.Clear();
        _selectedFrameIndices.Add(_canvas.CurrentFrameIndex);

        MarkDirty();
        RefreshAllViews();
    }

    private void ReorderFrames(int[] sourceIndices, int targetIndex)
    {
        if (_document is null || sourceIndices.Length == 0)
        {
            return;
        }

        var sorted = sourceIndices.Where(i => i >= 0 && i < _document.Frames.Count).Distinct().OrderBy(i => i).ToArray();
        if (sorted.Length == 0)
        {
            return;
        }

        targetIndex = Math.Clamp(targetIndex, 0, _document.Frames.Count - 1);
        if (targetIndex >= sorted.First() && targetIndex <= sorted.Last())
        {
            return;
        }

        var moving = sorted.Select(i => _document.Frames[i]).ToList();
        var remaining = _document.Frames.Where((_, i) => !sorted.Contains(i)).ToList();
        var adjustedTarget = targetIndex - sorted.Count(i => i < targetIndex);
        adjustedTarget = Math.Clamp(adjustedTarget, 0, remaining.Count);
        remaining.InsertRange(adjustedTarget, moving);

        _document.Frames.Clear();
        _document.Frames.AddRange(remaining);

        _selectedFrameIndices.Clear();
        for (var i = 0; i < moving.Count; i++)
        {
            _selectedFrameIndices.Add(adjustedTarget + i);
        }

        _canvas.CurrentFrameIndex = adjustedTarget;
        _canvas.ClearSelection();

        MarkDirty();
        RefreshAllViews();
    }

    private void DuplicateSelectedFrames()
    {
        if (_document is null || _selectedFrameIndices.Count == 0)
        {
            return;
        }

        var selected = _selectedFrameIndices.Where(i => i >= 0 && i < _document.Frames.Count).OrderBy(i => i).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var clones = selected.Select(i => _document.Frames[i].Clone($"Frame {_document.Frames.Count + 1}")).ToList();
        var insertIndex = selected.Last() + 1;
        _document.Frames.InsertRange(insertIndex, clones);

        _selectedFrameIndices.Clear();
        for (var i = 0; i < clones.Count; i++)
        {
            _selectedFrameIndices.Add(insertIndex + i);
        }

        _canvas.CurrentFrameIndex = insertIndex;
        MarkDirty();
        RefreshAllViews();
    }

    private void DeleteSelectedFrames()
    {
        if (_document is null || _selectedFrameIndices.Count == 0 || _document.Frames.Count <= 1)
        {
            return;
        }

        var selected = _selectedFrameIndices.Where(i => i >= 0 && i < _document.Frames.Count).OrderByDescending(i => i).ToArray();
        if (selected.Length == 0 || selected.Length >= _document.Frames.Count)
        {
            return;
        }

        foreach (var index in selected)
        {
            _document.Frames.RemoveAt(index);
        }

        var next = Math.Clamp(selected.Min(), 0, _document.Frames.Count - 1);
        _canvas.CurrentFrameIndex = next;
        _selectedFrameIndices.Clear();
        _selectedFrameIndices.Add(next);

        MarkDirty();
        RefreshAllViews();
    }

    private void MoveSelectedFrames(int direction)
    {
        if (_document is null || _selectedFrameIndices.Count == 0 || (direction != -1 && direction != 1))
        {
            return;
        }

        var selected = _selectedFrameIndices
            .Where(i => i >= 0 && i < _document.Frames.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        if (direction < 0)
        {
            if (selected[0] <= 0)
            {
                return;
            }

            foreach (var index in selected)
            {
                (_document.Frames[index - 1], _document.Frames[index]) = (_document.Frames[index], _document.Frames[index - 1]);
            }
        }
        else
        {
            if (selected[^1] >= _document.Frames.Count - 1)
            {
                return;
            }

            for (var i = selected.Count - 1; i >= 0; i--)
            {
                var index = selected[i];
                (_document.Frames[index], _document.Frames[index + 1]) = (_document.Frames[index + 1], _document.Frames[index]);
            }
        }

        _selectedFrameIndices.Clear();
        foreach (var index in selected)
        {
            _selectedFrameIndices.Add(index + direction);
        }

        _canvas.CurrentFrameIndex = Math.Clamp(_canvas.CurrentFrameIndex + direction, 0, _document.Frames.Count - 1);
        _canvas.ClearSelection();
        MarkDirty();
        RefreshAllViews();
    }

    private void AddLayer()
    {
        if (_document is null)
        {
            return;
        }

        var frame = _document.Frames[_canvas.CurrentFrameIndex];
        frame.Layers.Add(new PixelLayer(_document.Width, _document.Height, $"Layer {frame.Layers.Count + 1}"));
        _canvas.CurrentLayerIndex = frame.Layers.Count - 1;
        _canvas.ClearSelection();

        MarkDirty();
        RefreshAllViews();
    }

    private void DuplicateLayer()
    {
        if (_document is null)
        {
            return;
        }

        var frame = _document.Frames[_canvas.CurrentFrameIndex];
        var current = frame.Layers[_canvas.CurrentLayerIndex];
        frame.Layers.Insert(_canvas.CurrentLayerIndex + 1, current.Clone($"{current.Name} Copy"));
        _canvas.CurrentLayerIndex++;
        _canvas.ClearSelection();

        MarkDirty();
        RefreshAllViews();
    }

    private void DeleteLayer()
    {
        if (_document is null)
        {
            return;
        }

        var frame = _document.Frames[_canvas.CurrentFrameIndex];
        if (frame.Layers.Count <= 1)
        {
            return;
        }

        frame.Layers.RemoveAt(_canvas.CurrentLayerIndex);
        _canvas.CurrentLayerIndex = Math.Clamp(_canvas.CurrentLayerIndex, 0, frame.Layers.Count - 1);
        _canvas.ClearSelection();

        MarkDirty();
        RefreshAllViews();
    }

    private void MoveLayer(int direction)
    {
        if (_document is null)
        {
            return;
        }

        var frame = _document.Frames[_canvas.CurrentFrameIndex];
        var index = _canvas.CurrentLayerIndex;
        var target = index + direction;

        if (target < 0 || target >= frame.Layers.Count)
        {
            return;
        }

        (frame.Layers[index], frame.Layers[target]) = (frame.Layers[target], frame.Layers[index]);
        _canvas.CurrentLayerIndex = target;
        _canvas.ClearSelection();

        MarkDirty();
        RefreshAllViews();
    }

    private void StepPlayback()
    {
        if (_document is null || _document.Frames.Count == 0)
        {
            return;
        }

        var sequence = GetPlaybackSequenceIndices();
        if (sequence.Count == 0)
        {
            return;
        }

        if (sequence.Count == 1)
        {
            SetCurrentFrameForPlayback(sequence[0]);
            return;
        }

        var mode = _playModeCombo.SelectedIndex == 1 ? PlaybackMode.PingPong : PlaybackMode.Loop;
        var currentPos = sequence.IndexOf(_canvas.CurrentFrameIndex);
        if (currentPos < 0)
        {
            currentPos = 0;
            SetCurrentFrameForPlayback(sequence[currentPos]);
        }

        var nextPos = currentPos + _playDirection;

        if (mode == PlaybackMode.Loop)
        {
            nextPos = (currentPos + 1) % sequence.Count;
        }
        else
        {
            if (nextPos >= sequence.Count)
            {
                _playDirection = -1;
                nextPos = sequence.Count - 2;
            }
            else if (nextPos < 0)
            {
                _playDirection = 1;
                nextPos = 1;
            }
        }

        SetCurrentFrameForPlayback(sequence[nextPos]);
    }

    private void TogglePlayback()
    {
        if (_playButton.Checked)
        {
            _playButton.Text = "Stop";
            _playDirection = 1;
            _playbackTimer.Interval = Math.Max(16, 1000 / (int)_fpsInput.Value);
            _playbackTimer.Start();
        }
        else
        {
            StopPlayback();
        }
    }

    private void StopPlayback()
    {
        _playbackTimer.Stop();
        _playButton.Checked = false;
        _playButton.Text = "Play";
    }

    private void UpdatePlaybackRangeInputs()
    {
        if (_document is null || _document.Frames.Count == 0)
        {
            _playRangeStartInput.Minimum = 1;
            _playRangeStartInput.Maximum = 1;
            _playRangeStartInput.Value = 1;
            _playRangeEndInput.Minimum = 1;
            _playRangeEndInput.Maximum = 1;
            _playRangeEndInput.Value = 1;
            return;
        }

        var previousMax = (int)_playRangeEndInput.Maximum;
        var shouldFollowTail = _playRangeEndInput.Value == previousMax;
        var max = _document.Frames.Count;
        _playRangeStartInput.Maximum = max;
        _playRangeEndInput.Maximum = max;
        _playRangeStartInput.Value = Math.Clamp(_playRangeStartInput.Value, _playRangeStartInput.Minimum, _playRangeStartInput.Maximum);
        _playRangeEndInput.Value = shouldFollowTail
            ? max
            : Math.Clamp(_playRangeEndInput.Value, _playRangeEndInput.Minimum, _playRangeEndInput.Maximum);
        EnsurePlaybackRangeOrder();
    }

    private List<int> GetPlaybackSequenceIndices()
    {
        if (_document is null || _document.Frames.Count == 0)
        {
            return [];
        }

        var selected = _selectedFrameIndices
            .Where(i => i >= 0 && i < _document.Frames.Count)
            .Distinct()
            .OrderBy(i => i)
            .ToList();

        if (selected.Count > 1)
        {
            return selected;
        }

        var startIndex = Math.Clamp((int)_playRangeStartInput.Value - 1, 0, _document.Frames.Count - 1);
        var endIndex = Math.Clamp((int)_playRangeEndInput.Value - 1, startIndex, _document.Frames.Count - 1);
        var range = new List<int>(endIndex - startIndex + 1);
        for (var i = startIndex; i <= endIndex; i++)
        {
            range.Add(i);
        }

        return range;
    }

    private void SetCurrentFrameForPlayback(int index)
    {
        if (_document is null || index < 0 || index >= _document.Frames.Count)
        {
            return;
        }

        _canvas.CurrentFrameIndex = index;
        _canvas.CurrentLayerIndex = Math.Clamp(_canvas.CurrentLayerIndex, 0, _document.Frames[index].Layers.Count - 1);
        RefreshInspector();
        RefreshPreview();
        RefreshStatus();
        _suppressFrameDurationEvent = true;
        try
        {
            _frameDurationInput.Value = Math.Clamp(_document.Frames[index].DurationMs, (int)_frameDurationInput.Minimum, (int)_frameDurationInput.Maximum);
        }
        finally
        {
            _suppressFrameDurationEvent = false;
        }

        _canvas.Invalidate();
    }

    private void EnsurePlaybackRangeOrder()
    {
        if (_playRangeStartInput.Value > _playRangeEndInput.Value)
        {
            _playRangeEndInput.Value = _playRangeStartInput.Value;
        }
    }

    private void SaveProject()
    {
        if (_document is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentProjectPath))
        {
            SaveProjectAs();
            return;
        }

        try
        {
            PixelProjectSerializer.Save(_currentProjectPath, _document);
            _recentStore.Touch(_currentProjectPath);
            _autosaveNeeded = false;
            _recoveryStore.ClearSnapshot();
            _savedHistoryIndex = _historyIndex;
            RefreshWindowTitleFromHistory();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Save failed:\n{ex.Message}", "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveProjectAs()
    {
        if (_document is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PixelPalace Project|*.ppal|JSON|*.json",
            FileName = string.IsNullOrWhiteSpace(_currentProjectPath) ? "project.ppal" : Path.GetFileName(_currentProjectPath)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _currentProjectPath = dialog.FileName;
        SaveProject();
    }

    private void ExportFramePng()
    {
        if (_document is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = "frame.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var bitmap = _document.RenderFrameBitmap(_canvas.CurrentFrameIndex);
        bitmap.Save(dialog.FileName, ImageFormat.Png);
    }

    private void ExportSpriteSheet()
    {
        if (_document is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = "spritesheet.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var bitmap = _document.RenderSpriteSheet();
        bitmap.Save(dialog.FileName, ImageFormat.Png);
    }

    private void RefreshWindowTitle()
    {
        var fileName = string.IsNullOrWhiteSpace(_currentProjectPath) ? "Untitled" : Path.GetFileName(_currentProjectPath);
        Text = $"PixelPalace - {fileName}{(_isDirty ? " *" : string.Empty)}";
        _projectLabel.Text = fileName;
    }

    private void MarkDirty()
    {
        if (_historyRestoring)
        {
            return;
        }

        PushHistorySnapshot();
        _autosaveNeeded = true;
        RefreshWindowTitleFromHistory();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        if (!_isDirty)
        {
            _recoveryStore.ClearSnapshot();
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _playbackTimer.Stop();
        _autosaveTimer.Stop();
        _previewBox.Image?.Dispose();
        base.OnFormClosed(e);
    }

    private void TryWriteRecoverySnapshot()
    {
        if (_document is null || !_autosaveNeeded || !_isDirty)
        {
            return;
        }

        try
        {
            _recoveryStore.SaveSnapshot(_document, _currentProjectPath);
            _autosaveNeeded = false;
        }
        catch
        {
            // Recovery writes are best-effort.
        }
    }

    private void TryPromptRecoveryOnStartup()
    {
        if (!_recoveryStore.HasRecoverySnapshot())
        {
            return;
        }

        RecoverySnapshot? snapshot;
        try
        {
            snapshot = _recoveryStore.LoadSnapshot();
        }
        catch
        {
            _recoveryStore.ClearSnapshot();
            return;
        }

        if (snapshot is null)
        {
            return;
        }

        var source = string.IsNullOrWhiteSpace(snapshot.SourceProjectPath)
            ? "unsaved project"
            : Path.GetFileName(snapshot.SourceProjectPath);
        var savedAt = snapshot.SavedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var prompt = $"Recovered autosave found for {source} ({savedAt}).\n\nYes: Open recovery\nNo: Discard recovery";

        var result = MessageBox.Show(this, prompt, "Recovery Available", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result == DialogResult.Yes)
        {
            _document = snapshot.Document;
            _currentProjectPath = snapshot.SourceProjectPath;
            _autosaveNeeded = false;
            BindDocument();
            _savedHistoryIndex = -1;
            RefreshWindowTitleFromHistory();
            ShowEditor();
        }
        else
        {
            _recoveryStore.ClearSnapshot();
        }
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_isDirty)
        {
            return true;
        }

        var result = MessageBox.Show(this, "You have unsaved changes. Save before continuing?", "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (result == DialogResult.Cancel)
        {
            return false;
        }

        if (result == DialogResult.Yes)
        {
            SaveProject();
            return !_isDirty;
        }

        return true;
    }

    private void RefreshStatus()
    {
        if (_document is null)
        {
            _statusTool.Text = "Tool: -";
            _statusFrame.Text = "Frame -";
            return;
        }

        var mode = _playModeCombo.SelectedIndex == 1 ? "Ping-Pong" : "Loop";
        _statusTool.Text = $"Tool: {_canvas.Tool} | Brush {_canvas.BrushSize}px | {mode}";
        _statusFrame.Text = $"Frame {_canvas.CurrentFrameIndex + 1}/{_document.Frames.Count} | Layer {_canvas.CurrentLayerIndex + 1}";
    }

    private void ApplySelectionMutation(Func<bool> operation)
    {
        operation();
    }

    private void ApplyHexInputColor()
    {
        if (!TryParseHexColor(_hexColorInput.Text, out var color))
        {
            _hexColorInput.BackColor = Color.FromArgb(92, 38, 44);
            return;
        }

        _hexColorInput.BackColor = Color.FromArgb(28, 31, 46);
        SetPrimaryColor(color);
    }

    private void SyncHexControlsFromPrimaryColor()
    {
        var c = _canvas.PrimaryColor;
        _hexColorPreview.BackColor = c;
        _hexColorInput.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _hexColorInput.BackColor = Color.FromArgb(28, 31, 46);
        _pickerSyncMute = true;
        try
        {
            _hsvPicker.SelectedColor = c;
        }
        finally
        {
            _pickerSyncMute = false;
        }
    }

    private static bool TryParseHexColor(string input, out Color color)
    {
        color = Color.Black;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var hex = input.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length == 6)
        {
            if (!int.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) ||
                !int.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
                !int.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return false;
            }

            color = Color.FromArgb(r, g, b);
            return true;
        }

        if (hex.Length == 8)
        {
            if (!int.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var a) ||
                !int.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) ||
                !int.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
                !int.TryParse(hex.AsSpan(6, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                return false;
            }

            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }

    private void ReplacePrimaryWithSecondary(ReplaceScope scope)
    {
        if (_document is null)
        {
            return;
        }

        var source = _canvas.PrimaryColor.ToArgb();
        var target = _canvas.SecondaryColor;
        var changed = 0;

        IEnumerable<PixelFrame> frames = scope switch
        {
            ReplaceScope.Layer => [_document.Frames[_canvas.CurrentFrameIndex]],
            ReplaceScope.Frame => [_document.Frames[_canvas.CurrentFrameIndex]],
            _ => _document.Frames
        };

        foreach (var frame in frames)
        {
            IEnumerable<PixelLayer> layers = scope switch
            {
                ReplaceScope.Layer => [frame.Layers[_canvas.CurrentLayerIndex]],
                _ => frame.Layers
            };

            foreach (var layer in layers)
            {
                for (var y = 0; y < _document.Height; y++)
                {
                    for (var x = 0; x < _document.Width; x++)
                    {
                        var pixel = layer.GetPixel(x, y);
                        if (!pixel.HasValue || pixel.Value.ToArgb() != source)
                        {
                            continue;
                        }

                        layer.SetPixel(x, y, target);
                        changed++;
                    }
                }
            }
        }

        if (changed <= 0)
        {
            return;
        }

        MarkDirty();
        RefreshAllViews();
    }

    private void ResetHistory()
    {
        _history.Clear();
        _historyIndex = -1;
        _savedHistoryIndex = -1;
        PushHistorySnapshot();
        _savedHistoryIndex = _historyIndex;
        _isDirty = false;
    }

    private void PushHistorySnapshot()
    {
        if (_document is null || _historyRestoring)
        {
            return;
        }

        if (_historyIndex < _history.Count - 1)
        {
            if (_savedHistoryIndex > _historyIndex)
            {
                _savedHistoryIndex = -1;
            }

            _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
        }

        _history.Add(new HistoryEntry(
            _document.Clone(),
            Math.Clamp(_canvas.CurrentFrameIndex, 0, Math.Max(0, _document.Frames.Count - 1)),
            Math.Clamp(_canvas.CurrentLayerIndex, 0, Math.Max(0, _document.Frames[Math.Clamp(_canvas.CurrentFrameIndex, 0, _document.Frames.Count - 1)].Layers.Count - 1))));

        if (_history.Count > MaxHistoryEntries)
        {
            var overflow = _history.Count - MaxHistoryEntries;
            _history.RemoveRange(0, overflow);
            _historyIndex = Math.Max(-1, _historyIndex - overflow);
            _savedHistoryIndex = _savedHistoryIndex >= 0 ? _savedHistoryIndex - overflow : -1;
            if (_savedHistoryIndex < 0)
            {
                _savedHistoryIndex = -1;
            }
        }

        _historyIndex = _history.Count - 1;
        _isDirty = _historyIndex != _savedHistoryIndex;
    }

    private void Undo()
    {
        if (_historyIndex <= 0)
        {
            return;
        }

        _historyIndex--;
        RestoreHistoryEntry(_history[_historyIndex]);
    }

    private void Redo()
    {
        if (_historyIndex < 0 || _historyIndex >= _history.Count - 1)
        {
            return;
        }

        _historyIndex++;
        RestoreHistoryEntry(_history[_historyIndex]);
    }

    private void RestoreHistoryEntry(HistoryEntry entry)
    {
        _historyRestoring = true;
        try
        {
            _document = entry.Snapshot.Clone();
            _canvas.Document = _document;
            _canvas.CurrentFrameIndex = Math.Clamp(entry.FrameIndex, 0, _document.Frames.Count - 1);
            _canvas.CurrentLayerIndex = Math.Clamp(entry.LayerIndex, 0, _document.Frames[_canvas.CurrentFrameIndex].Layers.Count - 1);
            RefreshAllViews();
        }
        finally
        {
            _historyRestoring = false;
        }

        _isDirty = _historyIndex != _savedHistoryIndex;
        RefreshWindowTitleFromHistory();
    }

    private void RefreshWindowTitleFromHistory()
    {
        _isDirty = _historyIndex != _savedHistoryIndex;
        RefreshWindowTitle();
    }
}
