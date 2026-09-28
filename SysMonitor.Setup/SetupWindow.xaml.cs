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
    private readonly CheckBox _desktop = new() { Content = "สร้างไอคอนบนเดสก์ท็อป" };
    private readonly CheckBox _startMenu = new() { Content = "เพิ่มใน Start menu", IsChecked = true };
    private readonly CheckBox _autostart = new() { Content = "เปิดพร้อม Windows" };
    private readonly CheckBox _launch = new() { Content = "เปิดโปรแกรมหลังติดตั้ง", IsChecked = true };
    private readonly CheckBox _removeSettings = new() { Content = "ลบการตั้งค่าและ log ด้วย" };
    private readonly CheckBox _removeLegacy = new() { IsChecked = true };

    /// <summary>The retired builds -- Python/Tk and pre-4.0 C# -- if installed.</summary>
    private List<Legacy.Install> _legacies = new();

    public SetupWindow(bool uninstalling)
    {
        _uninstalling = uninstalling;
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
    }

    private void BuildInstall()
    {
        string? installed = Installer.InstalledVersion;
        bool update = installed is not null;

        Heading.Text = update ? "อัปเดต System Monitor" : "ติดตั้ง System Monitor";
        Subheading.Text = update
            ? $"พบเวอร์ชัน {installed} อยู่แล้ว จะแทนที่ด้วยเวอร์ชัน {Installer.Version}\n{Installer.InstallDir}"
            : $"ติดตั้งสำหรับผู้ใช้คนนี้เท่านั้น ไม่ต้องใช้สิทธิ์ผู้ดูแลระบบ\n{Installer.InstallDir}";
        ActionButton.Content = update ? "อัปเดต" : "ติดตั้ง";

        // An upgrade must not silently undo choices the user already made.
        _desktop.IsChecked = update ? System.IO.File.Exists(Installer.DesktopShortcut) : false;
        _startMenu.IsChecked = update ? System.IO.File.Exists(Installer.StartMenuShortcut) : true;
        // On a first install there is no Run value to read, so asking the
        // registry would always answer "no" and leave the box clear. An
        // upgrade still follows whatever the user chose last time, or someone
        // who turned it off would have it turned back on by every update.
        _autostart.IsChecked = update ? Installer.AutostartEnabled() : true;

        // The old builds install alongside this one and start themselves too,
        // so a machine with both opens two widgets every morning and the older
        // window lands on top. Every install looks for them.
        _legacies = Legacy.Find();
        if (_legacies.Count > 0)
        {
            _removeLegacy.Content = "ถอนรุ่นเก่า (เวอร์ชัน "
                + string.Join(", ", _legacies.Select(legacy => legacy.Version))
                + ") ออกด้วย";
        }

        Options.Children.Add(_desktop);
        Options.Children.Add(_startMenu);
        Options.Children.Add(_autostart);
        Options.Children.Add(_launch);
        if (_legacies.Count > 0)
        {
            Options.Children.Add(_removeLegacy);
        }

        if (!Installer.HasDesktopRuntime())
        {
            Status.Text = "ไม่พบ .NET 8 Desktop Runtime บนเครื่องนี้ — "
                        + "ติดตั้งได้ แต่โปรแกรมจะยังเปิดไม่ได้จนกว่าจะลง runtime";
        }
    }

    private void BuildUninstall()
    {
        Heading.Text = "ถอนการติดตั้ง System Monitor";
        Subheading.Text = "จะลบเฉพาะไฟล์ที่ตัวติดตั้งสร้างขึ้นเท่านั้น "
                        + "ไฟล์อื่นในโฟลเดอร์จะไม่ถูกแตะต้อง";
        ActionButton.Content = "ถอนการติดตั้ง";
        Options.Children.Add(_removeSettings);
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
                Relaunch.ToFinishUninstall(_removeSettings.IsChecked == true);
                Close();
                return;
            }

            Installer.Install(_desktop.IsChecked == true,
                              _startMenu.IsChecked == true,
                              _autostart.IsChecked == true);

            // After this build is in place, so a failure here leaves a working
            // installation behind rather than neither.
            if (_legacies.Count > 0 && _removeLegacy.IsChecked == true)
            {
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

            if (_launch.IsChecked == true)
            {
                Process.Start(new ProcessStartInfo(Installer.ExePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Installer.InstallDir,
                });
            }
            Close();
        }
        catch (Exception error)
        {
            Status.Text = "ไม่สำเร็จ: " + error.Message;
            ActionButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }
}
