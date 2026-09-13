using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace CopyToastApp
{
    public class Program
    {
        private const string MutexName = "Global\\CopyToast_Daemon_Mutex";

        [STAThread]
        public static void Main(string[] args)
        {
            string command = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (command == "--install" || command == "-i" || command == "/i")
            {
                Install(silent: true);
                return;
            }

            if (command == "--uninstall" || command == "-u" || command == "/u")
            {
                Uninstall(silent: true);
                return;
            }

            if (command == "--stop")
            {
                StopInstances();
                return;
            }

            if (command == "--start" || command == "--daemon" || command == "-s")
            {
                RunDaemon();
                return;
            }

            // Interactive Control Panel GUI (when double-clicked or run with no args)
            Application app = new Application();
            ControlPanelWindow controlPanel = new ControlPanelWindow();
            app.Run(controlPanel);
        }

        public static bool IsDaemonRunning()
        {
            try
            {
                using (var mutex = Mutex.OpenExisting(MutexName))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsInstalledInStartup()
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = Path.Combine(startupFolder, "CopyToast.lnk");
            return File.Exists(shortcutPath);
        }

        public static void Install(bool silent = false)
        {
            try
            {
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyToast");
                Directory.CreateDirectory(installDir);

                string destExe = Path.Combine(installDir, "CopyToast.exe");

                StopInstances(Process.GetCurrentProcess().Id);
                Thread.Sleep(200);

                if (!string.Equals(currentExe, destExe, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(currentExe, destExe, true);
                }

                // Create Startup shortcut
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolder, "CopyToast.lnk");
                CreateShortcut(shortcutPath, destExe, "--start", installDir, "Android-style copy & paste toast listener");

                // Start background listener
                StartDaemonProcess(destExe);

                if (!silent)
                {
                    MessageBox.Show(
                        "CopyToast has been installed and added to Windows Startup!\n\nThe listener is now active in the background.",
                        "CopyToast Installed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show("Installation failed: " + ex.Message, "CopyToast Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public static void Uninstall(bool silent = false)
        {
            try
            {
                StopInstances(Process.GetCurrentProcess().Id);

                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = Path.Combine(startupFolder, "CopyToast.lnk");
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }

                if (!silent)
                {
                    MessageBox.Show(
                        "CopyToast has been uninstalled from Windows Startup and stopped.",
                        "CopyToast Uninstalled",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show("Uninstall error: " + ex.Message, "CopyToast Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public static void StartDaemonProcess(string exePath = null)
        {
            if (exePath == null)
            {
                exePath = Process.GetCurrentProcess().MainModule.FileName;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--start",
                WorkingDirectory = Path.GetDirectoryName(exePath),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        public static void StopInstances(int excludePid = 0)
        {
            Process current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName("CopyToast"))
            {
                if (p.Id != current.Id && p.Id != excludePid)
                {
                    try { p.Kill(); } catch { }
                }
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string arguments, string workingDir, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = description;
            shortcut.WindowStyle = 7; // Minimized
            shortcut.Save();
        }

        private static void RunDaemon()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Already running
                    return;
                }

                Application app = new Application();
                ToastWindow window = new ToastWindow();
                app.Run(window);
            }
        }
    }

    public class ControlPanelWindow : Window
    {
        private TextBlock daemonStatusText;
        private TextBlock startupStatusText;
        private Button toggleDaemonBtn;
        private Button installStartupBtn;
        private DispatcherTimer refreshTimer;

        public ControlPanelWindow()
        {
            Title = "CopyToast Manager";
            Width = 440;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181818"));
            Foreground = Brushes.White;

            BuildUI();

            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            refreshTimer.Tick += (s, e) => RefreshStatus();
            refreshTimer.Start();

            RefreshStatus();
        }

        private void BuildUI()
        {
            Grid grid = new Grid { Margin = new Thickness(24) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status Box
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Buttons

            // Header
            StackPanel header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            TextBlock title = new TextBlock
            {
                Text = "CopyToast",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif")
            };
            TextBlock subtitle = new TextBlock
            {
                Text = "Android-Style Copy & Paste Toast Listener",
                FontSize = 12.5,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999999")),
                Margin = new Thickness(0, 2, 0, 0)
            };
            header.Children.Add(title);
            header.Children.Add(subtitle);
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // Status Box
            Border statusBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#242424")),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 18)
            };
            StackPanel statusPanel = new StackPanel();

            // Row 1: Daemon
            StackPanel row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row1.Children.Add(new TextBlock { Text = "Background Listener: ", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")), FontSize = 13 });
            daemonStatusText = new TextBlock { Text = "Checking...", FontWeight = FontWeights.SemiBold, FontSize = 13 };
            row1.Children.Add(daemonStatusText);

            // Row 2: Startup
            StackPanel row2 = new StackPanel { Orientation = Orientation.Horizontal };
            row2.Children.Add(new TextBlock { Text = "Windows Startup: ", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")), FontSize = 13 });
            startupStatusText = new TextBlock { Text = "Checking...", FontWeight = FontWeights.SemiBold, FontSize = 13 };
            row2.Children.Add(startupStatusText);

            statusPanel.Children.Add(row1);
            statusPanel.Children.Add(row2);
            statusBorder.Child = statusPanel;
            Grid.SetRow(statusBorder, 1);
            grid.Children.Add(statusBorder);

            // Action Buttons
            StackPanel buttonsPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

            toggleDaemonBtn = CreateStyledButton("Start Listener", "#0078D4");
            toggleDaemonBtn.Click += (s, e) =>
            {
                if (Program.IsDaemonRunning())
                {
                    Program.StopInstances();
                }
                else
                {
                    Program.StartDaemonProcess();
                }
                Thread.Sleep(200);
                RefreshStatus();
            };

            installStartupBtn = CreateStyledButton("Install to Windows Startup", "#2E7D32");
            installStartupBtn.Click += (s, e) =>
            {
                if (Program.IsInstalledInStartup())
                {
                    Program.Uninstall(silent: false);
                }
                else
                {
                    Program.Install(silent: false);
                }
                RefreshStatus();
            };

            Button testToastBtn = CreateStyledButton("Show Test Toast", "#383838");
            testToastBtn.Click += (s, e) =>
            {
                ToastWindow testWindow = new ToastWindow();
                testWindow.ShowToast("Copy", "Sample copied text for preview");
            };

            buttonsPanel.Children.Add(toggleDaemonBtn);
            buttonsPanel.Children.Add(installStartupBtn);
            buttonsPanel.Children.Add(testToastBtn);

            Grid.SetRow(buttonsPanel, 2);
            grid.Children.Add(buttonsPanel);

            Content = grid;
        }

        private Button CreateStyledButton(string text, string hexBg)
        {
            Button btn = new Button
            {
                Content = text,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexBg)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new Thickness(12, 6, 12, 6));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            template.VisualTree = border;
            btn.Template = template;
            return btn;
        }

        private void RefreshStatus()
        {
            bool isRunning = Program.IsDaemonRunning();
            bool isInstalled = Program.IsInstalledInStartup();

            if (isRunning)
            {
                daemonStatusText.Text = "● Running";
                daemonStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                toggleDaemonBtn.Content = "Stop Listener";
                toggleDaemonBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"));
            }
            else
            {
                daemonStatusText.Text = "● Stopped";
                daemonStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                toggleDaemonBtn.Content = "Start Listener";
                toggleDaemonBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
            }

            if (isInstalled)
            {
                startupStatusText.Text = "● Enabled";
                startupStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                installStartupBtn.Content = "Uninstall from Windows Startup";
                installStartupBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#616161"));
            }
            else
            {
                startupStatusText.Text = "● Disabled";
                startupStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9E9E9E"));
                installStartupBtn.Content = "Install to Windows Startup";
                installStartupBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
            }
        }
    }

    public class ToastWindow : Window
    {
        private TextBlock toastBadge;
        private TextBlock toastText;
        private Border badgeBorder;

        private DispatcherTimer hideTimer;
        private DispatcherTimer fadeInTimer;
        private DispatcherTimer fadeOutTimer;
        private DispatcherTimer pollTimer;

        private string lastClipboardText = "";
        private bool lastPasteKeyDown = false;
        private int lastActionTimestamp = 0;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        public ToastWindow()
        {
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            Opacity = 0;
            Focusable = false;

            BuildUI();
            InitTimers();

            SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int curStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, curStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };

            Loaded += (s, e) =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        lastClipboardText = Clipboard.GetText();
                    }
                }
                catch { }

                ShowToast("System", "Copy & Paste Toast Active");
                pollTimer.Start();
            };
        }

        private void BuildUI()
        {
            Border rootBorder = new Border
            {
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E81E1E1E")),
                Padding = new Thickness(18, 8, 18, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.45,
                    Direction = 270,
                    Color = Colors.Black
                }
            };

            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            badgeBorder = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3322CC66")),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            toastBadge = new TextBlock
            {
                Text = "Copied",
                Foreground = Brushes.White,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeBorder.Child = toastBadge;

            toastText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F3F3")),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 480
            };

            panel.Children.Add(badgeBorder);
            panel.Children.Add(toastText);
            rootBorder.Child = panel;
            Content = rootBorder;
        }

        private void InitTimers()
        {
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            fadeOutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(18) };
            fadeInTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(14) };
            pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };

            hideTimer.Tick += (s, e) =>
            {
                hideTimer.Stop();
                fadeOutTimer.Start();
            };

            fadeOutTimer.Tick += (s, e) =>
            {
                if (Opacity > 0.06)
                {
                    Opacity -= 0.12;
                }
                else
                {
                    Opacity = 0;
                    fadeOutTimer.Stop();
                    Hide();
                }
            };

            fadeInTimer.Tick += (s, e) =>
            {
                if (Opacity < 0.94)
                {
                    Opacity += 0.16;
                }
                else
                {
                    Opacity = 1.0;
                    fadeInTimer.Stop();
                    hideTimer.Start();
                }
            };

            pollTimer.Tick += (s, e) =>
            {
                CheckEvents();
            };
        }

        private void CheckEvents()
        {
            try
            {
                int now = Environment.TickCount;

                // 1. Check Paste shortcut (Ctrl+V or Shift+Insert)
                bool ctrlDown = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool vDown = (GetAsyncKeyState(0x56) & 0x8000) != 0;
                bool shiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                bool insertDown = (GetAsyncKeyState(0x2D) & 0x8000) != 0;

                bool isPasteKey = (ctrlDown && vDown) || (shiftDown && insertDown);

                if (isPasteKey)
                {
                    if (!lastPasteKeyDown)
                    {
                        lastPasteKeyDown = true;
                        if (Math.Abs(now - lastActionTimestamp) > 250)
                        {
                            lastActionTimestamp = now;
                            string pasteText = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                            string snippet = FormatSnippet(pasteText);
                            ShowToast("Paste", string.IsNullOrEmpty(snippet) ? "Content from clipboard" : snippet);
                        }
                    }
                }
                else
                {
                    lastPasteKeyDown = false;
                }

                // 2. Check Copy changes
                if (Clipboard.ContainsText())
                {
                    string current = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(current) && current != lastClipboardText)
                    {
                        lastClipboardText = current;
                        if (Math.Abs(now - lastActionTimestamp) > 200)
                        {
                            lastActionTimestamp = now;
                            string snippet = FormatSnippet(current);
                            ShowToast("Copy", string.IsNullOrEmpty(snippet) ? "Content copied" : snippet);
                        }
                    }
                }
            }
            catch { }
        }

        public void ShowToast(string type, string msg)
        {
            if (type == "Paste")
            {
                toastBadge.Text = "Pasted";
                badgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3300AAFF"));
            }
            else if (type == "System")
            {
                toastBadge.Text = "Ready";
                badgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3322CC66"));
            }
            else
            {
                toastBadge.Text = "Copied";
                badgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3322CC66"));
            }

            toastText.Text = msg;
            UpdateLayout();

            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Left + ((workArea.Width - ActualWidth) / 2);
            Top = workArea.Bottom - ActualHeight - 40;

            hideTimer.Stop();
            fadeOutTimer.Stop();
            Show();
            fadeInTimer.Start();
        }

        private static string FormatSnippet(string text, int maxLen = 45)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string clean = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
            if (clean.Length > maxLen)
            {
                return clean.Substring(0, maxLen) + "...";
            }
            return clean;
        }
    }
}
