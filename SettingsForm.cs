using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Insomnia
{
    internal sealed class SettingsForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(38, 99, 190);
        private readonly ConfigurationController controller;
        private AppConfiguration baseline;
        private readonly CheckBox useSchedule = new CheckBox { Text = "Korzystaj z harmonogramu", AutoSize = true };
        private readonly DateTimePicker start = TimePicker("Początek harmonogramu");
        private readonly DateTimePicker end = TimePicker("Koniec harmonogramu");
        private readonly CheckBox[] dayBoxes = new[] {
            new CheckBox { Text = "Pn", AutoSize = true, Tag = ScheduleDays.Monday },
            new CheckBox { Text = "Wt", AutoSize = true, Tag = ScheduleDays.Tuesday },
            new CheckBox { Text = "Śr", AutoSize = true, Tag = ScheduleDays.Wednesday },
            new CheckBox { Text = "Czw", AutoSize = true, Tag = ScheduleDays.Thursday },
            new CheckBox { Text = "Pt", AutoSize = true, Tag = ScheduleDays.Friday },
            new CheckBox { Text = "Sb", AutoSize = true, Tag = ScheduleDays.Saturday },
            new CheckBox { Text = "Nd", AutoSize = true, Tag = ScheduleDays.Sunday }
        };
        private readonly RadioButton rbBlockWifi = new RadioButton { Text = "Wyłączaj program, gdy wykryto sieć z listy (np. w biurze)", AutoSize = true };
        private readonly RadioButton rbAllowWifi = new RadioButton { Text = "Działaj tylko wtedy, gdy wykryto sieć z listy (np. w domu)", AutoSize = true };
        private readonly ListView excluded = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Height = 138, Dock = DockStyle.Top };
        private readonly ListBox detected = new ListBox { Height = 108, Dock = DockStyle.Top, IntegralHeight = false, HorizontalScrollbar = true };
        private readonly TextBox ssid = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Nazwa sieci SSID" };
        private readonly Label statusLabel = TextLabel("");
        private readonly Label dirtyLabel = TextLabel("Brak niezapisanych zmian");
        private readonly Label diagnostics = TextLabel("Oczekiwanie na pierwszy skan.");
        private readonly Label lastScan = TextLabel("Ostatni udany skan: —");
        private readonly Button refresh = Button("Odśwież sieci");
        private readonly Button apply = Button("Zastosuj");
        private readonly Action<string> reportError;
        private bool discardOnClose;
        private WifiState wifi;
        public event EventHandler RefreshRequested;

        public SettingsForm(ConfigurationController controller, Action<string> reportError = null)
        {
            this.controller = controller;
            this.reportError = reportError ?? (message => MessageBox.Show(this, message, "Insomnia", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            baseline = controller.Current.Clone();
            SuspendLayout();
            Text = "Insomnia — Ustawienia";
            Icon = Properties.Resources.insomnia;
            Font = new Font("Segoe UI", 10F);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(246, 248, 251);
            ClientSize = new Size(660, 680);
            MinimumSize = new Size(620, 480);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading = Stack();
            heading.Padding = new Padding(20, 14, 20, 12);
            var title = TextLabel("Insomnia");
            title.Font = new Font(Font.FontFamily, 23, FontStyle.Bold);
            title.ForeColor = Accent;
            Add(heading, title);
            Add(heading, statusLabel);
            root.Controls.Add(heading, 0, 0);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 0, 20, 0) };
            var content = Stack();
            content.Dock = DockStyle.Top;
            content.Padding = new Padding(0, 0, 0, 16);
            scroll.Controls.Add(content);
            root.Controls.Add(scroll, 0, 1);

            Add(content, SectionTitle("Harmonogram"));
            Add(content, useSchedule);
            var times = Flow();
            times.Controls.Add(TextLabel("Od")); times.Controls.Add(start);
            times.Controls.Add(TextLabel("Do")); times.Controls.Add(end);
            Add(content, times);
            var daysFlow = Flow();
            daysFlow.Controls.Add(TextLabel("Dni:"));
            foreach (var cb in dayBoxes) daysFlow.Controls.Add(cb);
            Add(content, daysFlow);
            Add(content, TextLabel("Te same godziny oznaczają całą dobę. Obsługiwany jest zakres przez północ (godziny nocne przypisywane są do zmiany z wybranego dnia)."));

            Add(content, SectionTitle("Reguły sieci Wi-Fi"));
            Add(content, TextLabel("Wybierz zachowanie programu przy wykryciu sieci z poniższej listy:"));
            var wifiModeFlow = Flow();
            wifiModeFlow.Controls.Add(rbBlockWifi);
            wifiModeFlow.Controls.Add(rbAllowWifi);
            Add(content, wifiModeFlow);
            Add(content, TextLabel("Dodawaj do listy roboczej, a następnie wybierz Zastosuj lub Zapisz i zamknij."));
            excluded.Columns.Add("Zapisane / edytowane SSID", 340);
            excluded.Columns.Add("Widoczność", 210);
            excluded.AccessibleName = "Lista sieci Wi-Fi";
            Add(content, excluded);
            var entry = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1 };
            entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            entry.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var add = Button("Dodaj");
            add.Click += (s, e) => AddSsid(ssid.Text, true);
            entry.Controls.Add(ssid, 0, 0); entry.Controls.Add(add, 1, 0);
            Add(content, entry);
            var remove = Button("Usuń zaznaczoną");
            remove.Click += (s, e) => { if (excluded.SelectedItems.Count > 0) { excluded.Items.Remove(excluded.SelectedItems[0]); UpdateDirty(); } };
            Add(content, remove);

            Add(content, SectionTitle("Diagnostyka Wi-Fi"));
            Add(content, TextLabel("SSID wykryte w zasięgu — połączenie z siecią nie jest wymagane."));
            detected.AccessibleName = "Aktualnie wykryte sieci";
            Add(content, detected);
            var scanButtons = Flow();
            refresh.Click += (s, e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
            var addDetected = Button("Dodaj zaznaczoną do listy sieci");
            addDetected.Click += (s, e) => { if (detected.SelectedItem != null) AddSsid((string)detected.SelectedItem, false); else this.reportError("Zaznacz wykrytą sieć."); };
            scanButtons.Controls.Add(refresh); scanButtons.Controls.Add(addDetected);
            Add(content, scanButtons);
            Add(content, lastScan); Add(content, diagnostics);
            var location = Button("Ustawienia lokalizacji Windows");
            location.Click += (s, e) => {
                try { Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); }
                catch (Exception ex) { this.reportError("Nie można otworzyć ustawień: " + ex.Message); }
            };
            Add(content, location);

            var footer = new TableLayoutPanel {
                Name = "Footer",
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.White,
                Padding = new Padding(16, 6, 16, 6),
                Margin = Padding.Empty,
                ColumnCount = 2,
                RowCount = 1
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            dirtyLabel.Anchor = AnchorStyles.Left;
            dirtyLabel.Margin = new Padding(0, 0, 16, 0);
            footer.Controls.Add(dirtyLabel, 0, 0);

            var buttons = Flow();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Margin = Padding.Empty;
            buttons.Dock = DockStyle.Fill;
            var cancel = Button("Anuluj");
            var saveClose = Button("Zapisz i zamknij");
            saveClose.BackColor = Accent; saveClose.ForeColor = Color.White; saveClose.FlatStyle = FlatStyle.Flat;
            cancel.Click += (s, e) => { discardOnClose = true; Close(); };
            apply.Click += (s, e) => TryApply();
            saveClose.Click += (s, e) => { if (TryApply()) { discardOnClose = true; Close(); } };
            buttons.Controls.Add(cancel); buttons.Controls.Add(saveClose); buttons.Controls.Add(apply);
            footer.Controls.Add(buttons, 1, 0);

            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
            CancelButton = cancel;

            useSchedule.Checked = baseline.ScheduleEnabled;
            start.Value = DateTime.Today + baseline.ScheduleStart;
            end.Value = DateTime.Today + baseline.ScheduleEnd;
            SetSelectedDays(baseline.ScheduleDays);
            rbBlockWifi.Checked = (baseline.WifiMode == WifiRuleMode.BlockOnMatching);
            rbAllowWifi.Checked = (baseline.WifiMode == WifiRuleMode.AllowOnlyOnMatching);
            foreach (string name in baseline.ExcludedSsids) excluded.Items.Add(new ListViewItem(new[] { name, "Brak aktualnych danych" }));
            useSchedule.CheckedChanged += (s, e) => { UpdateSchedule(); UpdateDirty(); };
            start.ValueChanged += (s, e) => UpdateDirty();
            end.ValueChanged += (s, e) => UpdateDirty();
            foreach (var cb in dayBoxes) cb.CheckedChanged += (s, e) => UpdateDirty();
            rbBlockWifi.CheckedChanged += (s, e) => UpdateDirty();
            rbAllowWifi.CheckedChanged += (s, e) => UpdateDirty();
            ssid.TextChanged += (s, e) => UpdateDirty();
            UpdateSchedule();
            FormClosing += OnClosing;
            // Text wraps at the actual available width, including when DPI changes.
            content.SizeChanged += (s, e) => WrapLabels(content, content.ClientSize.Width - 12);
            heading.SizeChanged += (s, e) => WrapLabels(heading, heading.ClientSize.Width - heading.Padding.Horizontal - 12);
            ResumeLayout(true);
            UpdateDirty();
        }

        private static TableLayoutPanel Stack()
        {
            var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }
        private static void Add(TableLayoutPanel panel, Control control)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(control, 0, row);
        }
        private static FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 4, 0, 4) }; }
        private static Label TextLabel(string text) { return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 6), ForeColor = Color.FromArgb(42, 49, 62) }; }
        private Label SectionTitle(string text) { var label = TextLabel(text); label.Font = new Font(Font, FontStyle.Bold); label.ForeColor = Accent; label.Margin = new Padding(3, 16, 3, 8); return label; }
        private static Button Button(string text) { return new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(3, 3, 6, 3), UseVisualStyleBackColor = true }; }
        private static DateTimePicker TimePicker(string name) { return new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 108, AccessibleName = name }; }
        private static void WrapLabels(TableLayoutPanel panel, int width)
        {
            foreach (var label in panel.Controls.OfType<Label>()) label.MaximumSize = new Size(Math.Max(100, width), 0);
        }
        private void UpdateSchedule()
        {
            bool enabled = useSchedule.Checked;
            start.Enabled = end.Enabled = enabled;
            foreach (var cb in dayBoxes) cb.Enabled = enabled;
        }
        private ScheduleDays GetSelectedDays()
        {
            ScheduleDays result = ScheduleDays.None;
            foreach (var cb in dayBoxes) { if (cb.Checked) result |= (ScheduleDays)cb.Tag; }
            return result;
        }
        private void SetSelectedDays(ScheduleDays days)
        {
            foreach (var cb in dayBoxes)
            {
                var day = (ScheduleDays)cb.Tag;
                cb.Checked = (days & day) == day;
            }
        }
        private AppConfiguration Draft()
        {
            return new AppConfiguration { ScheduleEnabled = useSchedule.Checked,
                ScheduleStart = TimeSpan.FromMinutes(start.Value.Hour * 60 + start.Value.Minute),
                ScheduleEnd = TimeSpan.FromMinutes(end.Value.Hour * 60 + end.Value.Minute),
                ScheduleDays = GetSelectedDays(),
                WifiMode = rbAllowWifi.Checked ? WifiRuleMode.AllowOnlyOnMatching : WifiRuleMode.BlockOnMatching,
                ExcludedSsids = excluded.Items.Cast<ListViewItem>().Select(x => x.Text).ToList() };
        }
        internal bool HasChanges
        {
            get {
                var draft = Draft();
                return ssid.Text.Length != 0 || draft.ScheduleEnabled != baseline.ScheduleEnabled ||
                    draft.ScheduleStart != baseline.ScheduleStart || draft.ScheduleEnd != baseline.ScheduleEnd ||
                    draft.ScheduleDays != baseline.ScheduleDays || draft.WifiMode != baseline.WifiMode ||
                    !draft.ExcludedSsids.SequenceEqual(baseline.ExcludedSsids, StringComparer.Ordinal);
            }
        }
        private void UpdateDirty() { bool dirty = HasChanges; dirtyLabel.Text = dirty ? "Niezapisane zmiany" : "Brak niezapisanych zmian"; apply.Enabled = dirty; }
        internal bool AddSsid(string name, bool clearInput)
        {
            try
            {
                var draft = Draft();
                draft.ExcludedSsids.Add(name);
                draft.Validate();
                excluded.Items.Add(new ListViewItem(new[] { name, wifi == null ? "Brak aktualnych danych" : wifi.NetworkState(name, DateTimeOffset.UtcNow) }));
                if (clearInput) ssid.Clear();
                UpdateDirty();
                return true;
            }
            catch (ArgumentException ex) { reportError(ex.Message); return false; }
        }
        internal bool TryApply()
        {
            try
            {
                if (ssid.Text.Length != 0) { reportError("Najpierw dodaj wpisaną nazwę przyciskiem Dodaj albo wyczyść pole."); return false; }
                controller.Apply(Draft()); // Merges the CURRENT manual switch, then writes to disk.
                baseline = controller.Current.Clone();
                UpdateDirty();
                SettingsApplied?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex) { reportError("Nie zapisano ustawień. Spróbuj ponownie.\n\n" + ex.Message); return false; }
        }
        public event EventHandler SettingsApplied;
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (discardOnClose || !HasChanges) return;
            var choice = MessageBox.Show(this, "Zapisać niezapisane zmiany?\nTak — zapisz, Nie — odrzuć, Anuluj — wróć do edycji.",
                "Insomnia", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel || (choice == DialogResult.Yes && !TryApply())) e.Cancel = true;
        }
        public void UpdateStatus(AppStatus status, WifiState state, DateTimeOffset now)
        {
            wifi = state;
            statusLabel.Text = status.Description;
            refresh.Enabled = !state.Scanning;
            diagnostics.Text = state.Describe(now);
            lastScan.Text = "Ostatni udany skan: " + (state.LastSuccess.HasValue ? state.LastSuccess.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—");
            string selected = detected.SelectedItem as string;
            var networks = state.Networks(now);
            if (!networks.SequenceEqual(detected.Items.Cast<string>()))
            {
                detected.BeginUpdate(); detected.Items.Clear(); detected.Items.AddRange(networks); detected.SelectedItem = selected; detected.EndUpdate();
            }
            foreach (ListViewItem item in excluded.Items) item.SubItems[1].Text = state.NetworkState(item.Text, now);
        }
    }
}
