using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SysMonitor.Setup;

/// <summary>
/// The wizard. Thai first, matching the widget; one screen, no next/back.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly bool _uninstalling;
    private readonly InstallScope _scope;
    private readonly SetupArguments _arguments;
    private readonly InstallLayout _layout;
    private readonly CheckBox _desktop = new() { Content = "สร้างไอคอนบนเดสก์ท็อป" };
    private readonly CheckBox _startMenu = new() { Content = "เพิ่มใน Start menu", IsChecked = true };
    private readonly CheckBox _autostart = new() { Content = "เปิดพร้อม Windows" };
    private readonly CheckBox _launch = new() { Content = "เปิดโปรแกรมหลังติดตั้ง", IsChecked = true };
    private readonly CheckBox _removeSettings = new() { Content = "ลบการตั้งค่าและ log ด้วย" };
    private readonly CheckBox _removeLegacy = new() { IsChecked = true };
    private readonly CheckBox _admin = new()
    {
        Content = "ติดตั้งสำหรับทุกคนในเครื่อง (Program Files) และรันเป็นผู้ดูแลระบบ — ขอสิทธิ์ UAC ครั้งเดียว",
    };

    /// <summary>The retired builds -- Python/Tk and pre-4.0 C# -- if installed.</summary>
    private List<Legacy.Install> _legacies = new();

    public SetupWindow(bool uninstalling, InstallScope scope, SetupArguments arguments)
    {
        _uninstalling = uninstalling;
        _scope = scope;
        _arguments = arguments;
        _layout = InstallLayout.Create(scope);
        InitializeComponent();

        CancelButton.Click += (_, _) => Close();
        ActionButton.Click += (_, _) => Run();

        if (uninstalling)
        {
            BuildUninstall();
        }
        else
        {
            BuildInstall();
        }

        // The elevated copy a UAC prompt started: the choices were made in the
        // copy before it, so it goes straight to work.
        if (arguments.Run)
        {
            Loaded += (_, _) => Run();
        }
    }

    private void BuildInstall()
    {
        string? installed = Installer.InstalledVersionOf(_layout);
        bool update = installed is not null;
        bool machineWide = _scope == InstallScope.Admin;

        Heading.Text = update ? "อัปเดต System Monitor" : "ติดตั้ง System Monitor";
        string where = machineWide
            ? $"ติดตั้งสำหรับทุกคนในเครื่อง รันเป็นผู้ดูแลระบบ\n{_layout.InstallDir}"
            : $"ติดตั้งสำหรับผู้ใช้คนนี้เท่านั้น ไม่ต้องใช้สิทธิ์ผู้ดูแลระบบ\n{_layout.InstallDir}";
        Subheading.Text = update
            ? $"พบเวอร์ชัน {installed} อยู่แล้ว จะแทนที่ด้วยเวอร์ชัน {Installer.Version}\n{_layout.InstallDir}"
            : where;
        ActionButton.Content = update ? "อัปเดต" : "ติดตั้ง";

        // An upgrade must not silently undo choices the user already made. Moving
        // from a per-user install to a machine-wide one is an upgrade of that
        // install, so its choices are the ones to start from.
        InstallLayout? from = update ? _layout : null;
        if (!update && _scope == InstallScope.Admin
            && InstallLayout.TryCreate(InstallScope.PerUser) is InstallLayout perUser
            && System.IO.File.Exists(perUser.ExePath))
        {
            from = perUser;
        }
        _desktop.IsChecked = _arguments.Desktop
            ?? (from is not null ? System.IO.File.Exists(from.DesktopShortcut) : false);
        _startMenu.IsChecked = _arguments.StartMenu
            ?? (from is not null ? System.IO.File.Exists(from.StartMenuShortcut) : true);
        // On a first install there is no Run value to read, so asking the
        // registry would always answer "no" and leave the box clear. An
        // upgrade still follows whatever the user chose last time, or someone
        // who turned it off would have it turned back on by every update.
        _autostart.IsChecked = _arguments.Autostart
            ?? (from is not null ? WindowsInstallHost.Instance.IsAutostartEnabled(from) : true);
        _launch.IsChecked = _arguments.Run ? _arguments.Launch : true;

        // The old builds install alongside this one and start themselves too,
        // so a machine with both opens two widgets every morning and the older
        // window lands on top. Every install looks for them.
        //
        // Never from an elevated process: removing one runs the uninstall command
        // recorded in the user's own registry hive, which anything running as the
        // user could have written. Elevated, the wizard only says they are there.
        _legacies = Legacy.Find();
        if (_legacies.Count > 0 && !Elevation.IsElevated())
        {
            _removeLegacy.Content = "ถอนรุ่นเก่า (เวอร์ชัน "
                + string.Join(", ", _legacies.Select(legacy => legacy.Version))
                + ") ออกด้วย";
            _removeLegacy.IsChecked = true;
        }

        Options.Children.Add(_desktop);
        Options.Children.Add(_startMenu);
        Options.Children.Add(_autostart);
        Options.Children.Add(_launch);
        if (_legacies.Count > 0 && !Elevation.IsElevated())
        {
            Options.Children.Add(_removeLegacy);
        }
        if (_scope == InstallScope.PerUser)
        {
            // Offered, never assumed: choosing it is what asks for UAC.
            Options.Children.Add(_admin);
        }

        if (!Installer.HasDesktopRuntime())
        {
            Status.Text = "ไม่พบ .NET 8 Desktop Runtime บนเครื่องนี้ — "
                        + "ติดตั้งได้ แต่โปรแกรมจะยังเปิดไม่ได้จนกว่าจะลง runtime";
        }
        else if (_legacies.Count > 0 && Elevation.IsElevated())
        {
            Status.Text = "พบรุ่นเก่าของโปรแกรมนี้ — ถอนได้จาก Apps & features "
                        + "(ไม่ถอนให้ในโหมดผู้ดูแลระบบ)";
        }
    }

    private void BuildUninstall()
    {
        Heading.Text = "ถอนการติดตั้ง System Monitor";
        Subheading.Text = "จะลบเฉพาะไฟล์ที่ตัวติดตั้งสร้างขึ้นเท่านั้น "
                        + "ไฟล์อื่นในโฟลเดอร์จะไม่ถูกแตะต้อง";
        ActionButton.Content = "ถอนการติดตั้ง";
        Options.Children.Add(_removeSettings);
        _removeSettings.IsChecked = _arguments.RemoveSettings;
        Status.Text = "ไฟล์ตัวถอนการติดตั้งเองจะถูกลบหลังรีสตาร์ท Windows";
    }

    private void Run()
    {
        ActionButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        try
        {
            if (_uninstalling)
            {
                Relaunch.ToFinishUninstall(_removeSettings.IsChecked == true, _scope);
                Close();
                return;
            }

            var options = new InstallOptions(_desktop.IsChecked == true,
                                             _startMenu.IsChecked == true,
                                             _autostart.IsChecked == true);
            InstallScope target = _scope == InstallScope.Admin || _admin.IsChecked == true
                ? InstallScope.Admin
                : InstallScope.PerUser;

            if (target == InstallScope.Admin && !Elevation.IsElevated())
            {
                _ = HandOverToElevatedCopy(options);
                return;
            }

            Installer.InstallScoped(target, options);
            InstallLayout installed = InstallLayout.Create(target);

            RemoveLegacyBuilds();

            if (_launch.IsChecked == true)
            {
                Process.Start(new ProcessStartInfo(installed.ExePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = installed.InstallDir,
                });
            }
            Close();
        }
        catch (Exception error)
        {
            Status.Text = "ไม่สำเร็จ: " + error.Message;
            // The copy that started this one is waiting for it, and an
            // unattended caller reads this.
            Environment.ExitCode = 1;
            ActionButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// After this build is in place, so a failure here leaves a working
    /// installation behind rather than neither. Only without administrator
    /// rights: see the comment where the legacy builds are listed.
    /// </summary>
    private void RemoveLegacyBuilds()
    {
        if (_legacies.Count == 0 || _removeLegacy.IsChecked != true || Elevation.IsElevated())
        {
            return;
        }
        Status.Text = "กำลังถอนรุ่นเก่า...";
        bool allRemoved = true;
        foreach (Legacy.Install legacy in _legacies)
        {
            if (!Legacy.Remove(legacy))
            {
                allRemoved = false;
            }
        }
        if (!allRemoved)
        {
            Status.Text = "ถอนรุ่นเก่าไม่สำเร็จ — ถอนเองได้จาก Apps & features";
        }
    }

    /// <summary>
    /// A machine-wide install needs administrator rights, so this copy hands
    /// the choices to a copy started with a UAC prompt and waits for it. If the
    /// user declines the prompt nothing has changed and the wizard stays open.
    ///
    /// What must not happen with administrator rights is done here, after the
    /// elevated copy has succeeded: removing the per-user install it replaces
    /// and the retired legacy builds both delete or run things from folders and
    /// registry keys the user controls.
    /// </summary>
    private async Task HandOverToElevatedCopy(InstallOptions options)
    {
        var handover = new SetupArguments(
            Silent: false,
            Admin: true,
            Desktop: options.DesktopShortcut,
            StartMenu: options.StartMenu,
            Autostart: options.Autostart,
            Launch: _launch.IsChecked == true,
            Run: true,
            RemoveSettings: false,
            FinishPid: null);

        Status.Text = "กำลังติดตั้งในหน้าต่างผู้ดูแลระบบ...";
        int? code = await Task.Run(() => Elevation.RelaunchAndWait(handover.ToArguments()));
        if (code == 0)
        {
            App.TryMigrate();
            RemoveLegacyBuilds();
            Close();
            return;
        }
        Status.Text = code is null
            ? "ยกเลิกการขอสิทธิ์ผู้ดูแลระบบ — ยังไม่ได้ติดตั้งอะไร"
            : "การติดตั้งแบบผู้ดูแลระบบไม่สำเร็จ — ดูข้อความในหน้าต่างผู้ดูแลระบบที่ปิดไป";
        ActionButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
    }
}
