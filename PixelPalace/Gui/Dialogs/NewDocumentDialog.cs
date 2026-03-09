using PixelPalace.Gui.Controls;

namespace PixelPalace.Gui.Dialogs;

public sealed class NewDocumentDialog : Form
{
    private readonly NumericUpDown _widthInput = new() { Minimum = 4, Maximum = 1024, Value = 32, Dock = DockStyle.Fill, ForeColor = Color.White, BackColor = Color.FromArgb(28, 31, 46), BorderStyle = BorderStyle.FixedSingle };
    private readonly NumericUpDown _heightInput = new() { Minimum = 4, Maximum = 1024, Value = 32, Dock = DockStyle.Fill, ForeColor = Color.White, BackColor = Color.FromArgb(28, 31, 46), BorderStyle = BorderStyle.FixedSingle };

    public int CanvasWidth => (int)_widthInput.Value;
    public int CanvasHeight => (int)_heightInput.Value;

    public NewDocumentDialog()
    {
        Text = "New Pixel Project";
        ClientSize = new Size(560, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(20, 22, 33);

        var root = new GradientPanel
        {
            Dock = DockStyle.Fill,
            StartColor = Color.FromArgb(29, 33, 56),
            EndColor = Color.FromArgb(18, 20, 33),
            Angle = 120f,
            Padding = new Padding(16)
        };

        var card = new NeonCard { Dock = DockStyle.Fill, BorderColor = Color.FromArgb(108, 152, 255) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12),
            BackColor = Color.FromArgb(24, 27, 40)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "Create New Pixel Project",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(236, 240, 255),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);

        layout.Controls.Add(new Label { Text = "Canvas Width", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(212, 220, 246), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        layout.Controls.Add(_widthInput, 1, 1);

        layout.Controls.Add(new Label { Text = "Canvas Height", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(212, 220, 246), TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        layout.Controls.Add(_heightInput, 1, 2);

        var presets = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(0, 2, 0, 2)
        };
        presets.Controls.Add(BuildPresetButton("16x16", 16, 16));
        presets.Controls.Add(BuildPresetButton("32x32", 32, 32));
        presets.Controls.Add(BuildPresetButton("64x64", 64, 64));
        presets.Controls.Add(BuildPresetButton("128x128", 128, 128));
        presets.Controls.Add(BuildPresetButton("256x256", 256, 256));
        layout.Controls.Add(presets, 0, 3);
        layout.SetColumnSpan(presets, 2);

        layout.Controls.Add(new Label
        {
            Text = "Tip: 32x32 or 64x64 is a great default for character sprites.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(164, 174, 209),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 4);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 4)!, 2);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        var create = new CandyButton
        {
            Text = "Create Project",
            AccentColor = Color.FromArgb(100, 147, 255),
            MinimumSize = new Size(130, 36),
            DialogResult = DialogResult.OK
        };

        var cancel = new Button
        {
            Text = "Cancel",
            Width = 96,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(72, 78, 112),
            ForeColor = Color.White,
            DialogResult = DialogResult.Cancel
        };
        cancel.FlatAppearance.BorderSize = 0;

        actions.Controls.Add(create);
        actions.Controls.Add(cancel);

        layout.Controls.Add(actions, 0, 5);
        layout.SetColumnSpan(actions, 2);

        card.Controls.Add(layout);
        root.Controls.Add(card);
        Controls.Add(root);

        AcceptButton = create;
        CancelButton = cancel;
    }

    private Button BuildPresetButton(string text, int width, int height)
    {
        var button = new Button
        {
            Text = text,
            Width = 90,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(69, 76, 113),
            ForeColor = Color.White
        };

        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) =>
        {
            _widthInput.Value = width;
            _heightInput.Value = height;
        };

        return button;
    }
}
