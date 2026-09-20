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

        Heading.Text = update ? "อัปเดต SysMonitor" : "ติดตั้ง SysMonitor";
        Subheading.Text = update
            ? $"พบเวอร์ชัน {installed} อยู่แล้ว จะแทนที่ด้วยเวอร์ชัน {Installer.Version}\n{Installer.InstallDir}"
            : $"ติดตั้งสำหรับผู้ใช้คนนี้เท่านั้น ไม่ต้องใช้สิทธิ์ผู้ดูแลระบบ\n{Installer.InstallDir}";
        ActionButton.Content = update ? "อัปเดต" : "ติดตั้ง";

        // An upgrade must not silently undo choices the user already made.
        _desktop.IsChecked = update ? System.IO.File.Exists(Installer.DesktopShortcut) : false;
        _startMenu.IsChecked = update ? System.IO.File.Exists(Installer.StartMenuShortcut) : true;
        _autostart.IsChecked = Installer.AutostartEnabled();

        Options.Children.Add(_desktop);
        Options.Children.Add(_startMenu);
        Options.Children.Add(_autostart);
        Options.Children.Add(_launch);

        if (!Installer.HasDesktopRuntime())
        {
            Status.Text = "ไม่พบ .NET 8 Desktop Runtime บนเครื่องนี้ — "
                        + "ติดตั้งได้ แต่โปรแกรมจะยังเปิดไม่ได้จนกว่าจะลง runtime";
        }
    }

    private void BuildUninstall()
    {
        Heading.Text = "ถอนการติดตั้ง SysMonitor";
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
