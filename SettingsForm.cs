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
        private readonly CheckBox cbSimMouseMove = new CheckBox { Text = "Mikro-ruchy myszą (Microsoft Teams, Slack, Skype, wygaszacz)", AutoSize = true, AccessibleName = "Mikro-ruchy myszą" };
        private readonly CheckBox cbSimF15 = new CheckBox { Text = "Niewidoczny klawisz F15 (dyskretny impuls bez ruszania kursorem)", AutoSize = true, AccessibleName = "Niewidoczny klawisz F15" };
        private readonly CheckBox cbSimMouseWheel = new CheckBox { Text = "Kółko myszy (subtelna aktywność dla przeglądarek i dokumentów)", AutoSize = true, AccessibleName = "Kółko myszy" };
        private readonly CheckBox cbSimAltTab = new CheckBox { Text = "Przełączanie okien Alt+Tab (dla narzędzi monitorujących, np. Time Doctor)", AutoSize = true, AccessibleName = "Przełączanie okien Alt+Tab" };
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

            var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(16, 0, 16, 0), Padding = new Point(12, 6) };
            var tabRules = new TabPage { Text = "Harmonogram i Wi-Fi", BackColor = Color.FromArgb(246, 248, 251), Padding = new Padding(8, 8, 8, 8), UseVisualStyleBackColor = false };
            var tabSimulation = new TabPage { Text = "Symulacja i Zgodność", BackColor = Color.FromArgb(246, 248, 251), Padding = new Padding(8, 8, 8, 8), UseVisualStyleBackColor = false };
            tabs.TabPages.Add(tabRules);
            tabs.TabPages.Add(tabSimulation);
            root.Controls.Add(tabs, 0, 1);

            // Tab 1: Harmonogram i Wi-Fi
            var scrollRules = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 12, 0) };
            var contentRules = Stack();
            contentRules.Dock = DockStyle.Top;
            contentRules.Padding = new Padding(0, 0, 0, 16);
            scrollRules.Controls.Add(contentRules);
            tabRules.Controls.Add(scrollRules);

            Add(contentRules, SectionTitle("Harmonogram"));
            Add(contentRules, useSchedule);
            var times = Flow();
            times.Controls.Add(TextLabel("Od")); times.Controls.Add(start);
            times.Controls.Add(TextLabel("Do")); times.Controls.Add(end);
            Add(contentRules, times);
            var daysFlow = Flow();
            daysFlow.Controls.Add(TextLabel("Dni:"));
            foreach (var cb in dayBoxes) daysFlow.Controls.Add(cb);
            Add(contentRules, daysFlow);
            Add(contentRules, TextLabel("Te same godziny oznaczają całą dobę. Obsługiwany jest zakres przez północ (godziny nocne przypisywane są do zmiany z wybranego dnia)."));

            Add(contentRules, SectionTitle("Reguły sieci Wi-Fi"));
            Add(contentRules, TextLabel("Wybierz zachowanie programu przy wykryciu sieci z poniższej listy:"));
            var wifiModeFlow = Flow();
            wifiModeFlow.Controls.Add(rbBlockWifi);
            wifiModeFlow.Controls.Add(rbAllowWifi);
            Add(contentRules, wifiModeFlow);
            Add(contentRules, TextLabel("Dodawaj do listy roboczej, a następnie wybierz Zastosuj lub Zapisz i zamknij."));
            excluded.Columns.Add("Zapisane / edytowane SSID", 340);
            excluded.Columns.Add("Widoczność", 210);
            excluded.AccessibleName = "Lista sieci Wi-Fi";
            Add(contentRules, excluded);
            var entry = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1 };
            entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            entry.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var add = Button("Dodaj");
            add.Click += (s, e) => AddSsid(ssid.Text, true);
            entry.Controls.Add(ssid, 0, 0); entry.Controls.Add(add, 1, 0);
            Add(contentRules, entry);
            var remove = Button("Usuń zaznaczoną");
            remove.Click += (s, e) => { if (excluded.SelectedItems.Count > 0) { excluded.Items.Remove(excluded.SelectedItems[0]); UpdateDirty(); } };
            Add(contentRules, remove);

            Add(contentRules, SectionTitle("Diagnostyka Wi-Fi"));
            Add(contentRules, TextLabel("SSID wykryte w zasięgu — połączenie z siecią nie jest wymagane."));
            detected.AccessibleName = "Aktualnie wykryte sieci";
            Add(contentRules, detected);
            var scanButtons = Flow();
            refresh.Click += (s, e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
            var addDetected = Button("Dodaj zaznaczoną do listy sieci");
            addDetected.Click += (s, e) => { if (detected.SelectedItem != null) AddSsid((string)detected.SelectedItem, false); else this.reportError("Zaznacz wykrytą sieć."); };
            scanButtons.Controls.Add(refresh); scanButtons.Controls.Add(addDetected);
            Add(contentRules, scanButtons);
            Add(contentRules, lastScan); Add(contentRules, diagnostics);
            var location = Button("Ustawienia lokalizacji Windows");
            location.Click += (s, e) => {
                try { Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); }
                catch (Exception ex) { this.reportError("Nie można otworzyć ustawień: " + ex.Message); }
            };
            Add(contentRules, location);

            // Tab 2: Symulacja i Zgodność
            var scrollSimulation = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 12, 0) };
            var contentSimulation = Stack();
            contentSimulation.Dock = DockStyle.Top;
            contentSimulation.Padding = new Padding(0, 0, 0, 16);
            scrollSimulation.Controls.Add(contentSimulation);
            tabSimulation.Controls.Add(scrollSimulation);

            Add(contentSimulation, SectionTitle("Działania podtrzymujące status"));
            Add(contentSimulation, TextLabel("Wybierz działania, które program ma wykonywać podczas symulacji aktywności (gdy nie pracujesz):"));

            var actionsStack = Stack();
            actionsStack.Padding = new Padding(8, 4, 8, 4);
            Add(actionsStack, cbSimMouseMove);
            Add(actionsStack, cbSimF15);
            Add(actionsStack, cbSimMouseWheel);
            Add(actionsStack, cbSimAltTab);
            Add(contentSimulation, actionsStack);

            Add(contentSimulation, TextLabel("Wskazówka: Opcja Alt+Tab jest domyślnie wyłączona, aby uniknąć zmiany aktywnego okna podczas prezentacji lub pracy w programach pełnoekranowych. Pozostałe 3 działania są w pełni dyskretne i niewidoczne dla otoczenia."));

            Add(contentSimulation, SectionTitle("Wspierane aplikacje i skuteczność"));
            Add(contentSimulation, TextLabel("🟢 Microsoft Teams (Desktop & Web)\nStały zielony status „Dostępny” (Available). Zapobiega przejściu w automatyczny żółty status „Zaraz wracam” (Away / Inactive)."));
            Add(contentSimulation, TextLabel("🟢 Slack / Skype / Zoom / Webex\nResetuje wewnętrzne liczniki bezczynności aplikacji, zapobiegając uśpieniu statusu obecności."));
            Add(contentSimulation, TextLabel("🟢 Blokada ekranu i wygaszacz Windows (GPO / Intune)\nZapobiega wylogowaniu i zablokowaniu stacji roboczej przez polityki korporacyjne IT (Active Directory / Intune)."));
            Add(contentSimulation, TextLabel("🟡 Ewidencja czasu pracy (Hubstaff, Time Doctor, DeskTime)\nRejestruje impulsy klawisza i myszy. W przypadku programów sprawdzających aktywność w konkretnym oknie, włącz opcję „Przełączanie okien Alt+Tab”."));

            Add(contentSimulation, SectionTitle("Zasada dyskrecji (Stealth)"));
            Add(contentSimulation, TextLabel("Insomnia symuluje aktywność wyłącznie wtedy, gdy przez co najmniej 25 sekund nie dotykasz myszy ani klawiatury. Gdy pracujesz przy komputerze, program natychmiast ustępuje miejsca i nie ingeruje w Twoje działania."));

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
            SetSelectedSimulationActions(baseline.SimulationActions);
            foreach (string name in baseline.ExcludedSsids) excluded.Items.Add(new ListViewItem(new[] { name, "Brak aktualnych danych" }));
            useSchedule.CheckedChanged += (s, e) => { UpdateSchedule(); UpdateDirty(); };
            start.ValueChanged += (s, e) => UpdateDirty();
            end.ValueChanged += (s, e) => UpdateDirty();
            foreach (var cb in dayBoxes) cb.CheckedChanged += (s, e) => UpdateDirty();
            rbBlockWifi.CheckedChanged += (s, e) => UpdateDirty();
            rbAllowWifi.CheckedChanged += (s, e) => UpdateDirty();
            cbSimMouseMove.CheckedChanged += (s, e) => UpdateDirty();
            cbSimF15.CheckedChanged += (s, e) => UpdateDirty();
            cbSimMouseWheel.CheckedChanged += (s, e) => UpdateDirty();
            cbSimAltTab.CheckedChanged += (s, e) => UpdateDirty();
            ssid.TextChanged += (s, e) => UpdateDirty();
            UpdateSchedule();
            FormClosing += OnClosing;
            // Text wraps at the actual available width, including when DPI changes.
            contentRules.SizeChanged += (s, e) => WrapLabels(contentRules, scrollRules.ClientSize.Width - 24);
            contentSimulation.SizeChanged += (s, e) => WrapLabels(contentSimulation, scrollSimulation.ClientSize.Width - 24);
            heading.SizeChanged += (s, e) => WrapLabels(heading, heading.ClientSize.Width - heading.Padding.Horizontal - 12);
            tabs.SelectedIndexChanged += (s, e) => {
                if (tabs.SelectedTab == tabRules) WrapLabels(contentRules, scrollRules.ClientSize.Width - 24);
                else if (tabs.SelectedTab == tabSimulation) WrapLabels(contentSimulation, scrollSimulation.ClientSize.Width - 24);
            };
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
        private static void WrapLabels(Control container, int width)
        {
            foreach (Control c in container.Controls)
            {
                if (c is Label label) label.MaximumSize = new Size(Math.Max(100, width), 0);
                else if (c.HasChildren) WrapLabels(c, width);
            }
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
        private SimulationActions GetSelectedSimulationActions()
        {
            SimulationActions actions = SimulationActions.None;
            if (cbSimMouseMove.Checked) actions |= SimulationActions.MouseMove;
            if (cbSimMouseWheel.Checked) actions |= SimulationActions.MouseWheel;
            if (cbSimF15.Checked) actions |= SimulationActions.F15Key;
            if (cbSimAltTab.Checked) actions |= SimulationActions.AltTab;
            return actions;
        }
        private void SetSelectedSimulationActions(SimulationActions actions)
        {
            cbSimMouseMove.Checked = (actions & SimulationActions.MouseMove) != 0;
            cbSimMouseWheel.Checked = (actions & SimulationActions.MouseWheel) != 0;
            cbSimF15.Checked = (actions & SimulationActions.F15Key) != 0;
            cbSimAltTab.Checked = (actions & SimulationActions.AltTab) != 0;
        }
        private AppConfiguration Draft()
        {
            return new AppConfiguration { ScheduleEnabled = useSchedule.Checked,
                ScheduleStart = TimeSpan.FromMinutes(start.Value.Hour * 60 + start.Value.Minute),
                ScheduleEnd = TimeSpan.FromMinutes(end.Value.Hour * 60 + end.Value.Minute),
                ScheduleDays = GetSelectedDays(),
                WifiMode = rbAllowWifi.Checked ? WifiRuleMode.AllowOnlyOnMatching : WifiRuleMode.BlockOnMatching,
                SimulationActions = GetSelectedSimulationActions(),
                ExcludedSsids = excluded.Items.Cast<ListViewItem>().Select(x => x.Text).ToList() };
        }
        internal bool HasChanges
        {
            get {
                var draft = Draft();
                return ssid.Text.Length != 0 || draft.ScheduleEnabled != baseline.ScheduleEnabled ||
                    draft.ScheduleStart != baseline.ScheduleStart || draft.ScheduleEnd != baseline.ScheduleEnd ||
                    draft.ScheduleDays != baseline.ScheduleDays || draft.WifiMode != baseline.WifiMode ||
                    draft.SimulationActions != baseline.SimulationActions ||
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
