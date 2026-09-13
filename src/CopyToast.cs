using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
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
                        "CopyToast has been installed and added to Windows Startup!\n\nThe universal listener is now active in the background.",
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
                    return; // Already running
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
            Width = 460;
            Height = 390;
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
                Text = "Universal Copy & Paste Toast Interceptor (Text, Files, Images)",
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

            Button testToastBtn = CreateStyledButton("Show Test Toast (Text / File / Image)", "#383838");
            testToastBtn.Click += (s, e) =>
            {
                ToastWindow testWindow = new ToastWindow();
                testWindow.ShowToast("Copied", "📄 sample_document.pdf (2.4 MB)", "#22CC66");
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

    public class ClipboardPayload
    {
        public string TypeDescription { get; set; }
        public string Snippet { get; set; }
        public string ContentHash { get; set; }
        public bool IsEmpty { get; set; }
    }

    public class ToastWindow : Window
    {
        private TextBlock toastBadge;
        private TextBlock toastText;
        private Border badgeBorder;

        private DispatcherTimer hideTimer;
        private DispatcherTimer fadeInTimer;
        private DispatcherTimer fadeOutTimer;

        private string lastContentHash = "";
        private int lastActionTimestamp = 0;
        private IntPtr windowHandle = IntPtr.Zero;
        private HwndSource hwndSource = null;

        // Low-level hooks
        private IntPtr keyboardHookId = IntPtr.Zero;
        private IntPtr winEventHookId = IntPtr.Zero;
        private LowLevelKeyboardProc keyboardProc;
        private WinEventDelegate winEventProc;

        #region Win32 Native APIs

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const uint EVENT_OBJECT_INVOKED = 0x8013;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        #endregion

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

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            windowHandle = new WindowInteropHelper(this).Handle;
            int curStyle = GetWindowLong(windowHandle, GWL_EXSTYLE);
            SetWindowLong(windowHandle, GWL_EXSTYLE, curStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            hwndSource = HwndSource.FromHwnd(windowHandle);
            if (hwndSource != null)
            {
                hwndSource.AddHook(HwndMessageHook);
            }

            // Register native OS-level clipboard listener (captures ALL copies, screenshots, explorer file copies, scripts)
            AddClipboardFormatListener(windowHandle);

            // Install low-level keyboard hook (captures Ctrl+V, Shift+Insert, Ctrl+Shift+V across entire OS)
            InstallKeyboardHook();

            // Install WinEvent hook for context menu paste clicks
            InstallWinEventHook();
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            // Initial hash cache
            var initialPayload = ReadClipboardSafe();
            if (initialPayload != null)
            {
                lastContentHash = initialPayload.ContentHash;
            }

            ShowToast("Ready", "Universal Copy & Paste Interceptor Active", "#22CC66");
        }

        private void OnClosed(object sender, EventArgs e)
        {
            if (windowHandle != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(windowHandle);
            }

            if (hwndSource != null)
            {
                hwndSource.RemoveHook(HwndMessageHook);
            }

            UninstallKeyboardHook();
            UninstallWinEventHook();
        }

        private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardUpdated();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnClipboardUpdated()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int now = Environment.TickCount;
                if (Math.Abs(now - lastActionTimestamp) < 120)
                {
                    return;
                }

                var payload = ReadClipboardSafe();
                if (payload == null || payload.IsEmpty) return;

                if (payload.ContentHash == lastContentHash && !string.IsNullOrEmpty(lastContentHash))
                {
                    return;
                }

                lastContentHash = payload.ContentHash;
                lastActionTimestamp = now;

                ShowToast(payload.TypeDescription, payload.Snippet, "#22CC66");
            }), DispatcherPriority.Normal);
        }

        private void OnPasteDetected()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                int now = Environment.TickCount;
                if (Math.Abs(now - lastActionTimestamp) < 180)
                {
                    return;
                }
                lastActionTimestamp = now;

                var payload = ReadClipboardSafe();
                string snippet = (payload != null && !payload.IsEmpty) ? payload.Snippet : "Clipboard Content";

                ShowToast("Pasted", snippet, "#00AAFF");
            }), DispatcherPriority.Normal);
        }

        #region Universal Clipboard Reader

        private ClipboardPayload ReadClipboardSafe()
        {
            // Retry loop to prevent external process locking collisions (CLIPBRD_E_CANT_OPEN)
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    IDataObject data = Clipboard.GetDataObject();
                    if (data == null) return new ClipboardPayload { IsEmpty = true };

                    // 1. Files / Folders (Windows Explorer copy, 7-zip, desktop)
                    if (data.GetDataPresent(DataFormats.FileDrop))
                    {
                        StringCollection files = Clipboard.GetFileDropList();
                        if (files != null && files.Count > 0)
                        {
                            string firstFile = Path.GetFileName(files[0]);
                            if (string.IsNullOrEmpty(firstFile)) firstFile = files[0];

                            string snippet;
                            if (files.Count == 1)
                            {
                                bool isDir = Directory.Exists(files[0]);
                                snippet = (isDir ? "📁 " : "📄 ") + firstFile;
                            }
                            else
                            {
                                snippet = string.Format("📁 {0} files ({1}, +{2} more)", files.Count, firstFile, files.Count - 1);
                            }

                            string hash = "FILES:" + files.Count + ":" + files[0];
                            return new ClipboardPayload { TypeDescription = "Copied", Snippet = snippet, ContentHash = hash, IsEmpty = false };
                        }
                    }

                    // 2. Images & Screenshots (Snipping tool Win+Shift+S, PrtScn, Paint, Browser Copy Image)
                    if (data.GetDataPresent(DataFormats.Bitmap) || Clipboard.ContainsImage())
                    {
                        BitmapSource img = Clipboard.GetImage();
                        string snippet = "🖼️ Image";
                        string hash = "IMAGE";
                        if (img != null)
                        {
                            snippet = string.Format("🖼️ Image ({0} × {1})", img.PixelWidth, img.PixelHeight);
                            hash = string.Format("IMAGE:{0}x{1}", img.PixelWidth, img.PixelHeight);
                        }
                        return new ClipboardPayload { TypeDescription = "Copied", Snippet = snippet, ContentHash = hash, IsEmpty = false };
                    }

                    // 3. Audio Stream
                    if (Clipboard.ContainsAudio())
                    {
                        return new ClipboardPayload { TypeDescription = "Copied", Snippet = "🎵 Audio Stream", ContentHash = "AUDIO", IsEmpty = false };
                    }

                    // 4. Plain Text & Rich Text / Code / URLs
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            string trimmed = text.Trim();
                            string icon = "📝 ";
                            if (Regex.IsMatch(trimmed, @"^https?://", RegexOptions.IgnoreCase))
                            {
                                icon = "🔗 ";
                            }
                            else if (trimmed.Contains("\n") || trimmed.Contains("{") || trimmed.Contains("class ") || trimmed.Contains("def "))
                            {
                                icon = "💻 ";
                            }

                            string cleanSnippet = FormatSnippet(trimmed);
                            string hash = "TEXT:" + text.GetHashCode() + ":" + text.Length;

                            return new ClipboardPayload { TypeDescription = "Copied", Snippet = icon + cleanSnippet, ContentHash = hash, IsEmpty = false };
                        }
                    }

                    return new ClipboardPayload { IsEmpty = true };
                }
                catch (COMException)
                {
                    Thread.Sleep(20);
                }
                catch (Exception)
                {
                    // Fallthrough
                }
            }

            return new ClipboardPayload { IsEmpty = true };
        }

        #endregion

        #region Keyboard and WinEvent Hooks

        private void InstallKeyboardHook()
        {
            keyboardProc = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void UninstallKeyboardHook()
        {
            if (keyboardHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(keyboardHookId);
                keyboardHookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);

                bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;

                // Ctrl+V or Ctrl+Shift+V or Shift+Insert or Ctrl+Alt+V
                bool isCtrlV = ctrl && (vkCode == 0x56);
                bool isShiftInsert = shift && (vkCode == 0x2D);

                if (isCtrlV || isShiftInsert)
                {
                    OnPasteDetected();
                }
            }

            return CallNextHookEx(keyboardHookId, nCode, wParam, lParam);
        }

        private void InstallWinEventHook()
        {
            try
            {
                winEventProc = WinEventCallback;
                winEventHookId = SetWinEventHook(EVENT_OBJECT_INVOKED, EVENT_OBJECT_INVOKED, IntPtr.Zero, winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            }
            catch { }
        }

        private void UninstallWinEventHook()
        {
            if (winEventHookId != IntPtr.Zero)
            {
                try { UnhookWinEvent(winEventHookId); } catch { }
                winEventHookId = IntPtr.Zero;
            }
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Detect context menu item invocations
            if (eventType == EVENT_OBJECT_INVOKED)
            {
                // Inspect if clipboard is read or active immediately around context menu invoke
                // Trigger quick check
            }
        }

        #endregion

        #region UI Rendering

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
                MaxWidth = 520
            };

            panel.Children.Add(badgeBorder);
            panel.Children.Add(toastText);
            rootBorder.Child = panel;
            Content = rootBorder;
        }

        private void InitTimers()
        {
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            fadeOutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            fadeInTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(14) };

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
        }

        public void ShowToast(string badge, string msg, string badgeHexColor = "#22CC66")
        {
            toastBadge.Text = badge;
            badgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeHexColor.StartsWith("#33") ? badgeHexColor : "#33" + badgeHexColor.TrimStart('#')));

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
            string clean = Regex.Replace(text.Trim(), @"\s+", " ");
            if (clean.Length > maxLen)
            {
                return clean.Substring(0, maxLen) + "...";
            }
            return clean;
        }

        #endregion
    }
}
