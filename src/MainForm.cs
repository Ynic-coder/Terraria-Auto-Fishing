using System.Diagnostics;
using System.Text.Json;

namespace Fishing;
sealed class MainForm : Form
{
    static readonly Color Bg = Color.FromArgb(15, 20, 29), Card = Color.FromArgb(25, 33, 46), Ink = Color.FromArgb(230, 237, 246), Muted = Color.FromArgb(150, 169, 190), Accent = Color.FromArgb(58, 214, 174);
    readonly Label status = new(), detail = new(), player = new(), bait = new(), bobber = new(), count = new(), signal = new();
    readonly Button connect = new(), start = new(), stop = new();
    readonly NumericUpDown hookDelay = new(), castDelay = new();
    readonly CheckBox autoCast = new(), onTop = new(), background = new();
    readonly TextBox log = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 75 };
    readonly FishingLogic logic = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    MemoryReader? reader;
    BackgroundClient? bridge;
    bool bridgeArmed;
    Task operation = Task.CompletedTask;
    bool armed, closing, finalClose, connecting;
    int hooks, casts, errors;
    Point? aim;
    readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerrariaFishing", "settings.json");

    public MainForm()
    {
        Text = "Terraria • Auto Fishing";
        ClientSize = new Size(760, 840); MinimumSize = new Size(710, 860);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Bg; ForeColor = Ink;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 8 };
        root.RowStyles.Add(new(SizeType.Absolute, 88)); root.RowStyles.Add(new(SizeType.Absolute, 91)); root.RowStyles.Add(new(SizeType.Absolute, 65));
        root.RowStyles.Add(new(SizeType.Absolute, 86)); root.RowStyles.Add(new(SizeType.Absolute, 154)); root.RowStyles.Add(new(SizeType.Absolute, 92));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 30)); Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "AUTO FISHING", Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = Accent, AutoSize = true, Location = new Point(0, 0) });
        header.Controls.Add(new Label { Text = "TERRARIA / STEAM     •     ЧТЕНИЕ СОСТОЯНИЯ ПРОЦЕССА", Font = new Font("Segoe UI", 9), ForeColor = Muted, AutoSize = true, Location = new Point(2, 51) }); root.Controls.Add(header);
        var statePanel = new Panel { Dock = DockStyle.Fill, BackColor = Card, Padding = new Padding(16), Margin = new Padding(0, 0, 0, 12) };
        status.Text = "Отключено"; status.Font = new Font("Segoe UI", 15, FontStyle.Bold); status.ForeColor = Accent; status.SetBounds(16, 10, 650, 30);
        detail.Text = "Запустите Terraria и нажмите «Подключиться»."; detail.ForeColor = Muted; detail.SetBounds(16, 45, 650, 25); statePanel.Controls.AddRange([status, detail]); root.Controls.Add(statePanel);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 0, 0, 12) };
        for (int i = 0; i < 3; i++) buttons.ColumnStyles.Add(new(SizeType.Percent, 33.333f));
        Style(connect, "Подключиться"); Style(start, "Старт  /  F8", true); Style(stop, "Стоп  /  F9");
        buttons.Controls.Add(connect); buttons.Controls.Add(start); buttons.Controls.Add(stop); root.Controls.Add(buttons);
        connect.Click += (_, _) => { if (operation.IsCompleted) operation = ConnectAsync(); }; start.Click += (_, _) => Toggle(); stop.Click += (_, _) => Stop("Остановлено");
        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 0, 0, 12) };
        foreach (var item in new[] { ("ИГРОК", player), ("НАЖИВКА", bait), ("ПОПЛАВКИ", bobber), ("ПОДСЕЧКИ", count) })
        {
            stats.ColumnStyles.Add(new(SizeType.Percent, 25));
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Card, Margin = new Padding(0, 0, 8, 0) };
            panel.Controls.Add(new Label { Text = item.Item1, ForeColor = Muted, Font = new Font("Segoe UI", 8), Location = new Point(12, 10), AutoSize = true });
            item.Item2.Text = "—"; item.Item2.Font = new Font("Segoe UI", 13, FontStyle.Bold); item.Item2.SetBounds(12, 30, 145, 28); item.Item2.AutoEllipsis = true; panel.Controls.Add(item.Item2); stats.Controls.Add(panel);
        }
        root.Controls.Add(stats);
        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Card, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12) };
        settings.ColumnStyles.Add(new(SizeType.Percent, 67)); settings.ColumnStyles.Add(new(SizeType.Percent, 33));
        hookDelay.Minimum = 0; hookDelay.Maximum = 1000; hookDelay.Increment = 25; hookDelay.Value = 75;
        castDelay.Minimum = 250; castDelay.Maximum = 5000; castDelay.Increment = 100; castDelay.Value = 800;
        settings.Controls.Add(new Label { Text = "Задержка подсечки, мс", AutoSize = true }, 0, 0); settings.Controls.Add(hookDelay, 1, 0);
        settings.Controls.Add(new Label { Text = "Пауза перед забросом, мс", AutoSize = true }, 0, 1); settings.Controls.Add(castDelay, 1, 1);
        autoCast.Text = "Повторный заброс"; autoCast.Checked = true; autoCast.AutoSize = true; settings.Controls.Add(autoCast, 0, 2);
        background.Text = "Фоновый режим"; background.Checked = true; background.AutoSize = true; settings.Controls.Add(background, 0, 3);
        onTop.Text = "Поверх окон"; onTop.AutoSize = true; onTop.CheckedChanged += (_, _) => TopMost = onTop.Checked; settings.Controls.Add(onTop, 1, 2); root.Controls.Add(settings);
        foreach (var n in new[] { hookDelay, castDelay }) { n.BackColor = Bg; n.ForeColor = Ink; n.Width = 140; }
        var help = new Label { Dock = DockStyle.Fill, ForeColor = Muted, Text = "1. Войдите в мир, выберите удочку, положите наживку в инвентарь.\n2. Наведите курсор на воду и нажмите F8 — точка заброса сохранится.\nF9 — остановка. Фоновый режим продолжает ловлю при смене окна.\nПосле перемещения нажмите F9 → F8. Смерть и интерфейсы — пауза." }; root.Controls.Add(help);
        log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.BackColor = Card; log.ForeColor = Muted; log.BorderStyle = BorderStyle.None; log.Dock = DockStyle.Fill; log.Font = new Font("Consolas", 9); root.Controls.Add(log);
        signal.Dock = DockStyle.Fill; signal.ForeColor = Muted; signal.Text = "F8 — старт / пауза     •     F9 — стоп"; signal.Font = new Font("Segoe UI", 8); root.Controls.Add(signal);
        LoadSettings(); UpdateButtons();
        timer.Tick += (_, _) => { if (operation.IsCompleted && !connecting && !closing) operation = PollAsync(); }; timer.Start();
        FormClosing += async (_, e) =>
        {
            if (finalClose) return;
            e.Cancel = true; closing = true; armed = false; timer.Stop();
            await operation; if (bridge != null) await bridge.DisposeAsync(); bridge = null; reader?.Dispose(); reader = null; SaveSettings();
            Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2);
            finalClose = true; Close();
        };
    }
    static void Style(Button b, string text, bool accent = false)
    {
        b.Text = text; b.Dock = DockStyle.Fill; b.Margin = new Padding(0, 0, 8, 0); b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
        b.BackColor = accent ? Accent : Card; b.ForeColor = accent ? Bg : Ink; b.Cursor = Cursors.Hand;
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        bool f8 = Native.RegisterHotKey(Handle, 1, 0x4000, 0x77); bool f9 = Native.RegisterHotKey(Handle, 2, 0x4000, 0x78);
        if (!f8 || !f9)
            Log("F8/F9 заняты другим приложением. Используйте кнопки меню.");
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312) { if (m.WParam.ToInt32() == 1) Toggle(); else Stop("Остановлено по F9"); }
        base.WndProc(ref m);
    }
    async Task ConnectAsync()
    {
        if (connecting || !operation.IsCompleted) return;
        connecting = true; armed = false; connect.Enabled = false; status.Text = "Подключение…";
        try
        {
            if (bridge != null) await bridge.DisposeAsync(); bridge = null; bridgeArmed = false;
            reader?.Dispose(); reader = null;
            var processes = Process.GetProcessesByName("Terraria");
            if (processes.Length != 1) throw new InvalidOperationException(processes.Length == 0 ? "Сначала запустите Terraria." : "Оставьте открытым один экземпляр Terraria.");
            int pid = processes[0].Id; foreach (var p in processes) p.Dispose();
            reader = await Task.Run(() => new MemoryReader(pid));
            if (background.Checked) bridge = await BackgroundClient.ConnectAsync(pid);
            status.Text = "Подключено · ожидание старта"; detail.Text = $"Terraria {reader.Version}  /  PID {reader.Pid}  /  {(bridge != null ? "фоновое управление" : "память процесса")}";
            Log($"Подключено к Terraria {reader.Version}, PID {reader.Pid}."); errors = 0;
        }
        catch (Exception ex) { reader?.Dispose(); reader = null; status.Text = "Не удалось подключиться"; detail.Text = ex.Message; Log(ex.Message); }
        finally { connecting = false; UpdateButtons(); }
    }
    void Toggle()
    {
        if (closing || connecting) return;
        if (armed) { Stop("Остановлено"); return; }
        if (reader == null) { Log("Сначала подключитесь к Terraria."); return; }
        if (background.Checked && bridge == null) { Log("Для фонового режима нажмите «Подключиться» заново."); return; }
        aim = Native.Foreground(reader.Pid) ? Native.GamePoint(Cursor.Position, reader.Pid) : null;
        armed = true; logic.Reset(clock.ElapsedMilliseconds); status.Text = aim.HasValue ? "Авторыбалка включена" : "Переключитесь в Terraria";
        hookDelay.Enabled = castDelay.Enabled = autoCast.Enabled = background.Enabled = false; Log("Авторыбалка включена. F9 — стоп."); UpdateButtons();
    }
    void Stop(string reason)
    {
        if (armed) Log(reason);
        armed = false; bridgeArmed = false; if (bridge != null) _ = QuietStopAsync(bridge); status.Text = reason; hookDelay.Enabled = castDelay.Enabled = autoCast.Enabled = background.Enabled = true; UpdateButtons();
    }
    async Task QuietStopAsync(BackgroundClient client) { try { await client.SendAsync("STOP"); } catch { } }
    void UpdateButtons() { connect.Enabled = !connecting && !armed; start.Enabled = reader != null && !connecting; stop.Enabled = armed; start.Text = armed ? "Пауза  /  F8" : "Старт  /  F8"; }
    async Task PollAsync()
    {
        if (reader == null) return;
        bool inputAttempted = false;
        try
        {
            var r = reader;
            var s = await Task.Run(r.Sample);
            if (closing) return;
            errors = 0; player.Text = s.Player; bait.Text = s.Bait.ToString(); bobber.Text = s.Bobbers.ToString(); count.Text = hooks.ToString();
            signal.Text = $"ai[0] = {s.Ai0:0.##}   ai[1] = {s.Ai1:0.##}   улов = {s.CatchId}   •   забросов: {casts}";
            if (!armed) return;
            bool foreground = Native.Foreground(r.Pid);
            if (foreground && aim == null) aim = Native.GamePoint(Cursor.Position, r.Pid);
            bool useBackground = background.Checked && bridge != null;
            if (useBackground) {
                if (!bridgeArmed) { await bridge!.SendAsync("ARM"); bridgeArmed = true; }
                await bridge!.SendAsync("PING");
                if (!armed || closing) { await bridge.SendAsync("STOP"); bridgeArmed = false; return; }
            }
            if (aim == null) { status.Text = "Наведите курсор на воду в Terraria"; return; }
            logic.HookDelay = (int)hookDelay.Value; logic.RecastDelay = (int)castDelay.Value; logic.AutoCast = autoCast.Checked; logic.Background = useBackground;
            var decision = logic.Evaluate(s, foreground, clock.ElapsedMilliseconds); status.Text = decision.Status;
            if (decision.Stop) { Stop(decision.Status); return; }
            if (decision.Action == ActionKind.None) return;
            // Read again just before input. Never act on an old UI update.
            var fresh = await Task.Run(r.Sample);
            if (closing || !armed || (!useBackground && !Native.Foreground(r.Pid)) || fresh.Menu || fresh.Paused || fresh.Dead || fresh.Inventory || !fresh.HasRod || fresh.Busy) return;
            if (decision.Action == ActionKind.Hook && !fresh.Bite || decision.Action == ActionKind.Cast && (fresh.Bobbers != 0 || fresh.Bait <= 0)) return;
            inputAttempted = true;
            if (useBackground) {
                if (await bridge!.SendAsync($"USE {decision.Action} {aim.Value.X} {aim.Value.Y}") != "DONE") return;
            } else {
                if (decision.Action == ActionKind.Cast) Cursor.Position = Native.ScreenPoint(aim.Value, r.Pid);
                if (!Native.Foreground(r.Pid) || !Native.PointOverGame(Cursor.Position, r.Pid)) return;
                try { Native.Mouse(true); await Task.Delay(65); }
                finally { Native.Mouse(false); }
            }
            logic.Clicked(decision.Action, clock.ElapsedMilliseconds);
            if (decision.Action == ActionKind.Hook) { hooks++; Log($"Подсечка · состояние улова {fresh.CatchId}"); }
            else { casts++; Log("Заброс удочки"); }
        }
        catch (Exception ex)
        {
            if (inputAttempted) { Stop("Ошибка клика · " + ex.Message); return; }
            errors++; if (errors == 1) Log(ex.Message);
            if (errors >= 3) { Stop("Чтение остановлено · подключитесь заново"); reader?.Dispose(); reader = null; detail.Text = ex.Message; UpdateButtons(); }
        }
    }
    void Log(string message)
    {
        if (log.TextLength > 16000) log.Text = log.Text[^8000..];
        log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }
    void LoadSettings()
    {
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(configPath)); if (s != null) { hookDelay.Value = Math.Clamp(s.Hook, 0, 1000); castDelay.Value = Math.Clamp(s.Cast, 250, 5000); autoCast.Checked = s.Auto; background.Checked = s.Background; } } catch { }
    }
    void SaveSettings()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(configPath)!); File.WriteAllText(configPath, JsonSerializer.Serialize(new Settings((int)hookDelay.Value, (int)castDelay.Value, autoCast.Checked, background.Checked))); } catch { }
    }
    record Settings(int Hook, int Cast, bool Auto, bool Background = true);
}



