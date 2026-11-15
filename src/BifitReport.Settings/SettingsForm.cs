using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;
using IOPath = System.IO.Path;

namespace BifitReport.Settings;

internal sealed class SettingsForm : Form
{
    private readonly ConfigFile _config;
    private readonly VkClient _vk;

    private TextBox _txtLogPath = null!;
    private TextBox _txtState = null!;
    private TextBox _txtStore = null!;
    private ComboBox _cmbMode = null!;
    private Label _lblService = null!;
    private TextBox _txtPeers = null!;
    private Label _lblToken = null!;
    private TextBox _outputVk = null!;
    private CheckBox _chkShiftOpen = null!;
    private CheckBox _chkShiftClose = null!;
    private CheckBox _chkCritical = null!;
    private CheckBox _chkErrorText = null!;
    private CheckBox _chkDetails = null!;
    private CheckBox _chkPeriodic = null!;
    private NumericUpDown _numCooldown = null!;
    private NumericUpDown _numPeriodic = null!;
    private NumericUpDown _numLines = null!;
    private NumericUpDown _numChars = null!;
    private CheckBox _chkArchive = null!;
    private TextBox _txtArchiveDest = null!;
    private TextBox _txtSources = null!;
    private NumericUpDown _numPoll = null!;
    private NumericUpDown _numAge = null!;
    private TextBox _outputDiag = null!;
    private TabControl _tabs = null!;

    public SettingsForm(ConfigFile config, Options options)
    {
        _config = config;
        _vk = new VkClient(config);

        BuildUi();
        LoadValues();

        if (!string.IsNullOrWhiteSpace(options.InitialTab))
        {
            SelectTab(options.InitialTab);
        }
    }

    private void BuildUi()
    {
        Text = "BifitReport — настройки";
        Size = new Size(880, 690);
        MinimumSize = new Size(840, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;

        _tabs = new TabControl
        {
            Location = new Point(10, 10),
            Size = new Size(844, 630),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        Controls.Add(_tabs);

        BuildMainTab();
        BuildVkTab();
        BuildReportsTab();
        BuildArchiveTab();
        BuildDiagnosticsTab();
    }

    private void BuildMainTab()
    {
        var tab = AddTab("Основное");

        AddLabel(tab, "Путь к журналу БИФИТ (desktop.log):", 10, 15);
        _txtLogPath = AddTextBox(tab, string.Empty, 10, 35);

        AddLabel(tab, "Каталог состояния (очередь, токен, статус):", 10, 70);
        _txtState = AddTextBox(tab, string.Empty, 10, 90);
        _txtState.ReadOnly = true;

        AddLabel(tab, "Название точки (попадает в сообщение как «Точка»):", 10, 125, 480);
        _txtStore = AddTextBox(tab, string.Empty, 10, 145, 480);

        AddLabel(tab, "Режим:", 520, 125, 80);
        _cmbMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(585, 145),
            Size = new Size(100, 24)
        };
        _cmbMode.Items.AddRange(["VK", "File"]);
        tab.Controls.Add(_cmbMode);

        _lblService = AddLabel(tab, string.Empty, 10, 185);

        AddButton(tab, "Сохранить", 10, 215, 130, SaveClick);
        AddButton(tab, "Сохранить и перезапустить", 150, 215, 210, SaveAndRestartClick);
        AddButton(tab, "Открыть каталог состояния", 370, 215, 200, OpenStateClick);

        var hint = new Label
        {
            Text = "Настройки сохраняются в appsettings.json службы. Для сохранения нужны права администратора."
                   + Environment.NewLine
                   + "После изменения пути журнала или получателей перезапустите службу.",
            Location = new Point(10, 265),
            Size = new Size(790, 60),
            ForeColor = Color.DimGray
        };
        tab.Controls.Add(hint);
    }

    private void BuildVkTab()
    {
        var tab = AddTab("VK");

        AddLabel(tab, "Получатели (peer_id через запятую; чат = 2000000000 + chat_id):", 10, 15);
        _txtPeers = AddTextBox(tab, string.Empty, 10, 35);

        _lblToken = AddLabel(tab, string.Empty, 10, 70);

        AddButton(tab, "Сохранить токен…", 10, 95, 160, SaveTokenClick);
        AddButton(tab, "Проверить VK", 180, 95, 130, CheckVkClick);
        AddButton(tab, "Отправить тест", 320, 95, 150, SendTestClick);

        _outputVk = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(10, 140),
            Size = new Size(790, 430),
            Font = new Font("Consolas", 9F)
        };
        tab.Controls.Add(_outputVk);
    }

    private void BuildReportsTab()
    {
        var tab = AddTab("Отчёты");

        _chkShiftOpen = AddCheckBox(tab, "Сообщать об открытии смены", 10, 15);
        _chkShiftClose = AddCheckBox(tab, "Отправлять итог при закрытии смены", 10, 45);
        _chkCritical = AddCheckBox(tab, "Немедленные аварийные уведомления", 10, 75);
        _chkErrorText = AddCheckBox(tab, "Добавлять очищенный текст ошибки", 10, 105);
        _chkDetails = AddCheckBox(tab, "Добавлять фрагмент стека/исключения", 10, 135);
        _chkPeriodic = AddCheckBox(tab, "Периодические промежуточные отчёты", 10, 165);

        _numCooldown = AddNumber(tab, "Повтор алерта, мин:", 10, 210, 1, 1440);
        _numPeriodic = AddNumber(tab, "Период сводки, мин:", 10, 250, 5, 1440);
        _numLines = AddNumber(tab, "Строк стека в алерте:", 10, 290, 0, 50);
        _numChars = AddNumber(tab, "Символов стека в алерте:", 10, 330, 100, 3000);

        var hint = new Label
        {
            Text = "Фрагмент стека очищается от токенов, телефонов, карт и e-mail."
                   + Environment.NewLine
                   + "Итоговое сообщение остаётся в пределах ~1,5 КБ при лимите VK 9000 символов.",
            Location = new Point(10, 380),
            Size = new Size(780, 50),
            ForeColor = Color.DimGray
        };
        tab.Controls.Add(hint);
    }

    private void BuildArchiveTab()
    {
        var tab = AddTab("Архив");

        _chkArchive = AddCheckBox(
            tab,
            "Копировать ротированные журналы в архив (до удаления в БИФИТ)",
            10,
            15,
            780);

        AddLabel(tab, "Каталог архива (лучше другой диск или сетевой ресурс):", 10, 50);
        _txtArchiveDest = AddTextBox(tab, string.Empty, 10, 70);

        AddLabel(tab, "Источники: по одному в строке, формат Имя|Путь", 10, 105);
        _txtSources = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(10, 125),
            Size = new Size(790, 120),
            Font = new Font("Consolas", 9F)
        };
        tab.Controls.Add(_txtSources);

        _numPoll = AddNumber(tab, "Период проверки, мин:", 10, 265, 1, 1440);
        _numAge = AddNumber(tab, "Не копировать файлы моложе, мин:", 10, 305, 0, 1440);

        var hint = new Label
        {
            Text = "Источник только читается, архив ничего не удаляет."
                   + Environment.NewLine
                   + "Служба работает от LocalSystem: для сетевого каталога этому аккаунту нужен доступ.",
            Location = new Point(10, 350),
            Size = new Size(780, 50),
            ForeColor = Color.DimGray
        };
        tab.Controls.Add(hint);
    }

    private void BuildDiagnosticsTab()
    {
        var tab = AddTab("Диагностика");

        AddButton(tab, "Обновить", 10, 10, 120, (_, _) => RefreshDiagnostics());

        _outputDiag = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Location = new Point(10, 50),
            Size = new Size(790, 520),
            Font = new Font("Consolas", 9F)
        };
        tab.Controls.Add(_outputDiag);

        Shown += (_, _) => RefreshDiagnostics();
    }

    private void LoadValues()
    {
        _txtLogPath.Text = _config.LogPath;
        _txtState.Text = _config.StateDirectory;
        _txtStore.Text = _config.StoreName;
        _cmbMode.SelectedItem = string.Equals(_config.Mode, "VK", StringComparison.OrdinalIgnoreCase)
            ? "VK"
            : "File";

        _txtPeers.Text = string.Join(", ", _config.GetPeers());
        _lblToken.Text = "Токен VK: " + TokenStore.Status(_config);
        _lblService.Text = "Служба BifitReport: " + GetServiceLabel();

        _chkShiftOpen.Checked = _config.GetBool("Reports", "SendShiftOpened", true);
        _chkShiftClose.Checked = _config.GetBool("Reports", "SendShiftClosed", true);
        _chkCritical.Checked = _config.GetBool("Reports", "SendImmediateCriticalErrors", true);
        _chkErrorText.Checked = _config.GetBool("Reports", "IncludeSanitizedErrorMessage", true);
        _chkDetails.Checked = _config.GetBool("Reports", "IncludeExceptionDetails", true);
        _chkPeriodic.Checked = _config.GetBool("Reports", "PeriodicSummaryEnabled", false);

        _numCooldown.Value = Clamp(_config.GetInt("Reports", "CriticalErrorCooldownMinutes", 10), 1, 1440);
        _numPeriodic.Value = Clamp(_config.GetInt("Reports", "PeriodicSummaryMinutes", 60), 5, 1440);
        _numLines.Value = Clamp(_config.GetInt("Reports", "MaxExceptionLines", 8), 0, 50);
        _numChars.Value = Clamp(_config.GetInt("Reports", "MaxExceptionCharacters", 900), 100, 3000);

        _chkArchive.Checked = _config.GetBool("Archive", "Enabled", false);
        _txtArchiveDest.Text = _config.GetString("Archive", "Destination");
        _txtSources.Text = string.Join(
            Environment.NewLine,
            _config.GetSources().Select(source => source.Name + "|" + source.Path));
        _numPoll.Value = Clamp(_config.GetInt("Archive", "PollIntervalMinutes", 60), 1, 1440);
        _numAge.Value = Clamp(_config.GetInt("Archive", "MinFileAgeMinutes", 10), 0, 1440);
    }

    private void SaveClick(object? sender, EventArgs e)
    {
        if (!ApplyForm() || !EnsureWritable()) return;

        try
        {
            _config.Save();
            ShowInfo("Настройки сохранены.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void SaveAndRestartClick(object? sender, EventArgs e)
    {
        if (!ApplyForm() || !EnsureWritable()) return;

        try
        {
            _config.Save();
            RestartService();
            _lblService.Text = "Служба BifitReport: " + GetServiceLabel();
            ShowInfo("Настройки сохранены, служба перезапущена.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void OpenStateClick(object? sender, EventArgs e)
    {
        var directory = _config.StateDirectory;
        if (!Directory.Exists(directory))
        {
            ShowError("Каталог состояния ещё не создан:" + Environment.NewLine + directory);
            return;
        }

        Process.Start("explorer.exe", "\"" + directory + "\"");
    }

    private void SaveTokenClick(object? sender, EventArgs e)
    {
        using var dialog = new TokenDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(dialog.Token))
        {
            ShowError("Токен не может быть пустым.");
            return;
        }

        try
        {
            TokenStore.Save(_config, dialog.Token);
            _lblToken.Text = "Токен VK: " + TokenStore.Status(_config);
            ShowInfo("Токен сохранён.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void CheckVkClick(object? sender, EventArgs e)
    {
        try
        {
            _outputVk.Text = _vk.Check();
        }
        catch (Exception exception)
        {
            _outputVk.Text = "ОШИБКА: " + exception.Message + Environment.NewLine;
        }
    }

    private void SendTestClick(object? sender, EventArgs e)
    {
        if (!TryParsePeers(_txtPeers.Text, out var peers, out var error))
        {
            ShowError(error);
            return;
        }

        if (peers.Length == 0)
        {
            ShowError("Сначала укажите peer_id.");
            return;
        }

        try
        {
            var messageId = _vk.SendTest(peers[0]);
            _outputVk.AppendText(
                $"Тест отправлен: peer_id={peers[0]}, message_id={messageId}{Environment.NewLine}");
        }
        catch (Exception exception)
        {
            _outputVk.AppendText("ОШИБКА: " + exception.Message + Environment.NewLine);
        }
    }

    private bool ApplyForm()
    {
        var logPath = _txtLogPath.Text.Trim();
        var stateDirectory = _txtState.Text.Trim();
        if (logPath.Length == 0)
        {
            ShowError("Путь к журналу не задан.");
            return false;
        }

        if (stateDirectory.Length == 0)
        {
            ShowError("Каталог состояния не задан.");
            return false;
        }

        if (!TryParsePeers(_txtPeers.Text, out var peers, out var error))
        {
            ShowError(error);
            return false;
        }

        var mode = _cmbMode.SelectedItem?.ToString() ?? "File";
        if (mode == "VK" && peers.Length == 0)
        {
            ShowError("Для режима VK нужен хотя бы один peer_id.");
            return false;
        }

        if (!TryParseSources(_txtSources.Text, out var sources, out error))
        {
            ShowError(error);
            return false;
        }

        if (_chkArchive.Checked && sources.Length == 0)
        {
            ShowError("Для архива добавьте хотя бы один источник.");
            return false;
        }

        if (_chkArchive.Checked && string.IsNullOrWhiteSpace(_txtArchiveDest.Text))
        {
            ShowError("Для архива укажите каталог назначения.");
            return false;
        }

        _config.SetString("Agent", "LogPath", logPath);
        _config.SetString("Agent", "StateDirectory", stateDirectory);
        _config.SetString("Reports", "StoreName", _txtStore.Text.Trim());
        _config.SetString("Delivery", "Mode", mode);
        _config.SetPeers(peers);

        _config.SetBool("Reports", "SendShiftOpened", _chkShiftOpen.Checked);
        _config.SetBool("Reports", "SendShiftClosed", _chkShiftClose.Checked);
        _config.SetBool("Reports", "SendImmediateCriticalErrors", _chkCritical.Checked);
        _config.SetBool("Reports", "IncludeSanitizedErrorMessage", _chkErrorText.Checked);
        _config.SetBool("Reports", "IncludeExceptionDetails", _chkDetails.Checked);
        _config.SetBool("Reports", "PeriodicSummaryEnabled", _chkPeriodic.Checked);
        _config.SetInt("Reports", "CriticalErrorCooldownMinutes", (int)_numCooldown.Value);
        _config.SetInt("Reports", "PeriodicSummaryMinutes", (int)_numPeriodic.Value);
        _config.SetInt("Reports", "MaxExceptionLines", (int)_numLines.Value);
        _config.SetInt("Reports", "MaxExceptionCharacters", (int)_numChars.Value);

        _config.SetBool("Archive", "Enabled", _chkArchive.Checked);
        _config.SetString("Archive", "Destination", _txtArchiveDest.Text.Trim());
        _config.SetSources(sources);
        _config.SetInt("Archive", "PollIntervalMinutes", (int)_numPoll.Value);
        _config.SetInt("Archive", "MinFileAgeMinutes", (int)_numAge.Value);

        return true;
    }

    private bool EnsureWritable()
    {
        if (Program.IsAdministrator()) return true;

        try
        {
            var probe = _config.Path + ".write-test";
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            ShowError("Нет прав на запись настроек. Запустите настройщик от имени администратора.");
            return false;
        }
    }

    private void RefreshDiagnostics()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Служба BifitReport: " + GetServiceLabel());
        builder.AppendLine("Токен VK: " + TokenStore.Status(_config));
        builder.AppendLine("Конфигурация: " + _config.Path);
        builder.AppendLine();

        AppendFile(builder, IOPath.Combine(_config.StateDirectory, "status.json"), "status.json");
        AppendFile(builder, IOPath.Combine(_config.StateDirectory, "archive-status.json"), "archive-status.json");

        var pending = CountFiles(IOPath.Combine(_config.StateDirectory, "queue", "pending"));
        var sent = CountFiles(IOPath.Combine(_config.StateDirectory, "queue", "sent"));
        builder.AppendLine($"Очередь: ожидает {pending}, отправлено {sent}");

        _outputDiag.Text = builder.ToString();
    }

    private string GetServiceLabel()
    {
        try
        {
            using var service = new ServiceController("BifitReport");
            return service.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "не установлена";
        }
        catch (Exception exception)
        {
            return "ошибка: " + exception.Message;
        }
    }

    private static void RestartService()
    {
        using var service = new ServiceController("BifitReport");
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }

        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    private static void AppendFile(StringBuilder builder, string path, string title)
    {
        if (File.Exists(path))
        {
            builder.AppendLine(title + ":");
            builder.AppendLine(File.ReadAllText(path, Encoding.UTF8));
        }
        else
        {
            builder.AppendLine(title + " отсутствует.");
        }

        builder.AppendLine();
    }

    private static int CountFiles(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json").Length : 0;

    private static bool TryParsePeers(string text, out long[] peers, out string error)
    {
        var values = new List<long>();
        foreach (var part in text.Split(
                     [',', ';', ' ', '\r', '\n', '\t'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (!long.TryParse(part.Trim(), out var peerId) || peerId <= 0)
            {
                peers = [];
                error = "Некорректный peer_id: " + part;
                return false;
            }

            values.Add(peerId);
        }

        peers = values.ToArray();
        error = string.Empty;
        return true;
    }

    private static bool TryParseSources(
        string text,
        out ArchiveSourceEntry[] sources,
        out string error)
    {
        var values = new List<ArchiveSourceEntry>();
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var separator = trimmed.IndexOf('|');
            if (separator <= 0 || separator >= trimmed.Length - 1)
            {
                sources = [];
                error = "Источник должен быть в формате Имя|Путь: " + trimmed;
                return false;
            }

            var name = trimmed.Substring(0, separator).Trim();
            var path = trimmed.Substring(separator + 1).Trim();
            if (name.Length == 0 || path.Length == 0)
            {
                sources = [];
                error = "Источник должен быть в формате Имя|Путь: " + trimmed;
                return false;
            }

            values.Add(new ArchiveSourceEntry { Name = name, Path = path });
        }

        sources = values.ToArray();
        error = string.Empty;
        return true;
    }

    private void SelectTab(string name)
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            if (string.Equals(page.Text, name, StringComparison.OrdinalIgnoreCase))
            {
                _tabs.SelectedTab = page;
                return;
            }
        }
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void ShowInfo(string message) =>
        MessageBox.Show(this, message, "BifitReport", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private TabPage AddTab(string text)
    {
        var page = new TabPage(text) { Padding = new Padding(10) };
        _tabs.TabPages.Add(page);
        return page;
    }

    private static Label AddLabel(Control parent, string text, int x, int y, int width = 700)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 20)
        };
        parent.Controls.Add(label);
        return label;
    }

    private static TextBox AddTextBox(Control parent, string text, int x, int y, int width = 780)
    {
        var box = new TextBox
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 24)
        };
        parent.Controls.Add(box);
        return box;
    }

    private static CheckBox AddCheckBox(
        Control parent,
        string text,
        int x,
        int y,
        int width = 500)
    {
        var box = new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 24)
        };
        parent.Controls.Add(box);
        return box;
    }

    private static Button AddButton(
        Control parent,
        string text,
        int x,
        int y,
        int width,
        EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 32)
        };
        button.Click += handler;
        parent.Controls.Add(button);
        return button;
    }

    private static NumericUpDown AddNumber(
        Control parent,
        string text,
        int x,
        int y,
        int minimum,
        int maximum)
    {
        AddLabel(parent, text, x, y - 1, 220);
        var number = new NumericUpDown
        {
            Location = new Point(x + 230, y - 3),
            Size = new Size(90, 24),
            Minimum = minimum,
            Maximum = maximum
        };
        parent.Controls.Add(number);
        return number;
    }

    private static decimal Clamp(int value, int minimum, int maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    private sealed class TokenDialog : Form
    {
        private readonly TextBox _textBox;

        public TokenDialog()
        {
            Text = "Токен сообщества VK";
            Size = new Size(560, 200);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9F);

            var label = new Label
            {
                Text = "Вставьте токен сообщества (право messages):",
                Location = new Point(12, 12),
                Size = new Size(520, 20)
            };
            _textBox = new TextBox
            {
                Location = new Point(12, 36),
                Size = new Size(520, 24),
                UseSystemPasswordChar = true
            };
            var hint = new Label
            {
                Text = "Токен шифруется DPAPI и сохраняется только на этом компьютере.",
                Location = new Point(12, 66),
                Size = new Size(520, 20),
                ForeColor = Color.DimGray
            };
            var ok = new Button
            {
                Text = "Сохранить",
                DialogResult = DialogResult.OK,
                Location = new Point(340, 110),
                Size = new Size(92, 30)
            };
            var cancel = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Location = new Point(440, 110),
                Size = new Size(92, 30)
            };

            Controls.AddRange([label, _textBox, hint, ok, cancel]);
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (_, _) => _textBox.Focus();
        }

        public string Token => _textBox.Text;
    }
}
