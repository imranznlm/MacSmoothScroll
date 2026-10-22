using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacSmoothScroll
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Two running copies would each swallow and re-emit every wheel event.
            using var mutex = new Mutex(true, @"Local\MacSmoothScroll.SingleInstance", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                using var icon = new NotifyIcon
                {
                    Icon = AppIcon.Tray,
                    Text = "MacSmoothScroll",
                    Visible = true
                };

                icon.ShowBalloonTip(
                    2500,
                    "MacSmoothScroll is already running",
                    "Look for its icon in the system tray.",
                    ToolTipIcon.Info);

                Thread.Sleep(2500);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SmoothScrollApp());
        }
    }

    public enum ScrollAxis
    {
        Vertical,
        Horizontal
    }

    /// <summary>The embedded multi-resolution icon, used by the exe, tray and windows.</summary>
    internal static class AppIcon    {
        private const string ResourceName = "MacSmoothScroll.app.ico";

        public static Icon Value { get; } = Load();

        public static Icon Tray { get; } = LoadSized(SystemInformation.SmallIconSize);

        private static Icon LoadSized(Size size)
        {
            try
            {
                return new Icon(Value, size);
            }
            catch
            {
                return Value;
            }
        }

        private static Icon Load()
        {
            try
            {
                using Stream? stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
                if (stream != null)
                    return new Icon(stream);
            }
            catch
            {
                // Corrupt or missing resource: fall back to the stock icon.
            }

            return SystemIcons.Application;
        }
    }

    /// <summary>
    /// Registers the app under HKCU\...\Run so Windows starts it at sign-in.
    /// This is the same entry Task Manager shows under "Startup apps".
    /// </summary>
    internal static class StartupEntry
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MacSmoothScroll";

        /// <summary>The running .exe, or null when hosted by the SDK (dotnet.exe).</summary>
        public static string? ExecutablePath
        {
            get
            {
                string? path = Environment.ProcessPath;
                return !string.IsNullOrEmpty(path) &&
                       path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : null;
            }
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                    return key?.GetValue(ValueName) is string value && value.Length > 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

                if (!enabled)
                {
                    key.DeleteValue(ValueName, false);
                    return true;
                }

                string? executable = ExecutablePath;
                if (executable == null)
                    return false;

                key.SetValue(ValueName, $"\"{executable}\"");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class SmoothScrollApp : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly MouseHook hook;
        private readonly ScrollAnimator animator;
        private readonly Settings settings;
        private readonly ToolStripMenuItem pauseItem;

        public SmoothScrollApp()
        {
            settings = Settings.Load();

            animator = new ScrollAnimator(settings);
            animator.Start();

            hook = new MouseHook(settings);
            hook.Wheel += animator.AddDelta;

            var menu = new ContextMenuStrip();
            pauseItem = new ToolStripMenuItem("Pause smooth scrolling") { CheckOnClick = true };
            pauseItem.CheckedChanged += (_, _) => SetPaused(pauseItem.Checked);
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApp());

            trayIcon = new NotifyIcon
            {
                Icon = AppIcon.Tray,
                Text = "MacSmoothScroll - smooth wheel scrolling",
                Visible = true,
                ContextMenuStrip = menu
            };
            trayIcon.DoubleClick += (_, _) => OpenSettings();

            try
            {
                hook.Install();
            }
            catch (Exception ex)
            {
                trayIcon.ShowBalloonTip(
                    5000,
                    "Smooth scrolling unavailable",
                    "The mouse hook could not be installed: " + ex.Message,
                    ToolTipIcon.Warning);
            }
        }

        private void SetPaused(bool paused)
        {
            hook.Enabled = !paused;
            if (paused)
                animator.Reset();
        }

        private void OpenSettings()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form is SettingsForm existing)
                {
                    existing.Activate();
                    return;
                }
            }

            new SettingsForm(settings).Show();
        }

        private void ExitApp()
        {
            trayIcon.Visible = false;
            settings.Save();
            hook.Dispose();
            animator.Stop();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hook.Dispose();
                animator.Stop();
                trayIcon.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public sealed class Settings
    {
        private readonly object sync = new object();
        private List<string> blacklist = new List<string>();

        public float Decay { get; set; } = 0.82f;
        public float Sensitivity { get; set; } = 1.2f;
        public int MinChunk { get; set; } = 12;
        public int MaxVelocity { get; set; } = 1200;
        public int IntervalMs { get; set; } = 16;

        /// <summary>
        /// Process names that should keep native (unsmoothed) scrolling.
        /// The getter returns a snapshot so callers cannot mutate the live list.
        /// </summary>
        public List<string> Blacklist
        {
            get
            {
                lock (sync)
                {
                    return new List<string>(blacklist);
                }
            }
            set
            {
                lock (sync)
                {
                    blacklist = value == null
                        ? new List<string>()
                        : value.FindAll(static name => !string.IsNullOrWhiteSpace(name));
                }
            }
        }

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MacSmoothScroll",
            "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    Settings loaded = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                    loaded.Sanitize();
                    return loaded;
                }
            }
            catch
            {
                // Corrupt or unreadable settings: fall back to defaults.
            }

            return new Settings();
        }

        public void Save()
        {
            try
            {
                string? folder = Path.GetDirectoryName(FilePath);
                if (folder != null)
                    Directory.CreateDirectory(folder);

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Never let a settings write take the app down.
            }
        }

        public Settings Clone()
        {
            lock (sync)
            {
                return new Settings
                {
                    Decay = Decay,
                    Sensitivity = Sensitivity,
                    MinChunk = MinChunk,
                    MaxVelocity = MaxVelocity,
                    IntervalMs = IntervalMs,
                    Blacklist = new List<string>(blacklist)
                };
            }
        }

        public bool IsBlacklisted(string? processName)
        {
            if (string.IsNullOrEmpty(processName))
                return false;

            lock (sync)
            {
                foreach (string entry in blacklist)
                {
                    if (entry.Length > 0 &&
                        string.Equals(NormalizeAppName(entry), processName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool AddToBlacklist(string rawName)
        {
            string name = NormalizeAppName(rawName);
            if (name.Length == 0)
                return false;

            lock (sync)
            {
                foreach (string entry in blacklist)
                {
                    if (string.Equals(NormalizeAppName(entry), name, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                blacklist.Add(name);
                return true;
            }
        }

        public bool RemoveFromBlacklist(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            lock (sync)
            {
                for (int i = blacklist.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(blacklist[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        blacklist.RemoveAt(i);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>"chrome.exe" and " chrome " both become "chrome".</summary>
        public static string NormalizeAppName(string? name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^4].Trim();
            return trimmed;
        }

        private void Sanitize()
        {
            Decay = Math.Clamp(Decay, 0.5f, 0.99f);
            Sensitivity = Math.Clamp(Sensitivity, 0.1f, 5f);
            MinChunk = Math.Clamp(MinChunk, 1, 200);
            MaxVelocity = Math.Clamp(MaxVelocity, 120, 5000);
            IntervalMs = Math.Clamp(IntervalMs, 4, 100);
        }
    }

    /// <summary>
    /// Turns raw wheel deltas into an inertial stream of small wheel steps.
    /// Runs on its own thread so it never competes with the UI thread.
    /// </summary>
    public sealed class ScrollAnimator
    {
        private const double BaselineIntervalMs = 16.0;
        private const float StopThreshold = 0.5f;

        private readonly Settings settings;
        private readonly object sync = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);

        private Thread? thread;
        private volatile bool running;
        private float verticalVelocity;
        private float horizontalVelocity;

        public ScrollAnimator(Settings settings)
        {
            this.settings = settings;
        }

        public void Start()
        {
            if (running)
                return;

            running = true;
            thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "ScrollAnimator",
                Priority = ThreadPriority.AboveNormal
            };
            thread.Start();
        }

        public void Stop()
        {
            if (!running)
                return;

            running = false;
            wake.Set();

            Thread? worker = thread;
            thread = null;
            worker?.Join(1000);
        }

        public void Reset()
        {
            lock (sync)
            {
                verticalVelocity = 0f;
                horizontalVelocity = 0f;
            }
        }

        public void AddDelta(int delta, ScrollAxis axis)
        {
            if (delta == 0)
                return;

            float sensitivity = Math.Clamp(settings.Sensitivity, 0.1f, 5f);
            float max = Math.Max(120, settings.MaxVelocity);
            float addition = delta * sensitivity;

            lock (sync)
            {
                if (axis == ScrollAxis.Horizontal)
                    horizontalVelocity = Math.Clamp(horizontalVelocity + addition, -max, max);
                else
                    verticalVelocity = Math.Clamp(verticalVelocity + addition, -max, max);
            }

            // Wake the loop so the first step is not delayed by a full tick.
            wake.Set();
        }

        private void Loop()
        {
            var clock = Stopwatch.StartNew();
            double nextTick = 0.0;

            while (running)
            {
                int interval = Math.Clamp(settings.IntervalMs, 4, 100);
                double now = clock.Elapsed.TotalMilliseconds;
                nextTick += interval;

                double wait = nextTick - now;
                if (wait > 0)
                {
                    wake.WaitOne((int)Math.Min(int.MaxValue, Math.Round(wait)));
                }
                else
                {
                    // We are behind schedule. Only resync after a real stall
                    // (sleep/resume) instead of trying to catch up forever.
                    if (-wait > 1000)
                        nextTick = now;

                    wake.WaitOne(0);
                }

                if (!running)
                    break;

                Tick();
            }
        }

        private void Tick()
        {
            Step(ref verticalVelocity, ScrollAxis.Vertical);
            Step(ref horizontalVelocity, ScrollAxis.Horizontal);
        }

        private void Step(ref float velocityField, ScrollAxis axis)
        {
            int interval = Math.Clamp(settings.IntervalMs, 4, 100);
            float decay = Math.Clamp(settings.Decay, 0.5f, 0.99f);
            int minChunk = Math.Max(1, settings.MinChunk);

            // Normalize inertia to a 60 Hz baseline so the Interval slider only
            // changes smoothness, not the total distance travelled.
            double factor = Math.Pow(decay, interval / BaselineIntervalMs);

            int chunk;
            lock (sync)
            {
                float velocity = velocityField;
                if (Math.Abs(velocity) < StopThreshold)
                {
                    velocityField = 0f;
                    return;
                }

                float next = (float)(velocity * factor);
                chunk = (int)Math.Round(velocity - next, MidpointRounding.AwayFromZero);

                if (Math.Abs(chunk) < minChunk)
                {
                    int lifted = (int)Math.Ceiling(Math.Abs(velocity));
                    chunk = Math.Sign(velocity) * Math.Min(minChunk, lifted);
                }

                if (Math.Abs(chunk) >= Math.Abs(velocity))
                {
                    // Final step: emit the remainder and stop instead of overshooting.
                    chunk = (int)velocity;
                    velocityField = 0f;
                }
                else
                {
                    velocityField = next;
                }
            }

            if (chunk != 0)
                SendScroll(chunk, axis);
        }

        private static void SendScroll(int delta, ScrollAxis axis)
        {
            var inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = unchecked((uint)delta),
                dwFlags = axis == ScrollAxis.Horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };

            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x01000;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }

    /// <summary>Small, blocking-safe helpers for asking Windows what sits under a point.</summary>
    internal static class WindowProbe
    {
        public static uint GetPidAt(int x, int y)
        {
            IntPtr hWnd = WindowFromPoint(new NativePoint { x = x, y = y });
            if (hWnd == IntPtr.Zero)
                return 0;

            GetWindowThreadProcessId(hWnd, out uint pid);
            return pid;
        }

        public static string? GetProcessNameAt(int x, int y) => GetProcessName(GetPidAt(x, y));

        public static string? GetProcessName(uint pid)
        {
            if (pid == 0)
                return null;

            try
            {
                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch
            {
                return null;
            }
        }

        public static bool TryGetCursorPosition(out int x, out int y)
        {
            if (GetCursorPos(out NativePoint point))
            {
                x = point.x;
                y = point.y;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int x;
            public int y;
        }
    }

    /// <summary>
    /// Global low-level mouse hook. It swallows physical wheel events and forwards
    /// their deltas to the animator, which re-emits them as smoothed input.
    /// The hook callback must stay fast: any process lookup that could block is
    /// pushed to a background resolver.
    /// </summary>
    public sealed class MouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_MOUSEHWHEEL = 0x020E;

        private const uint LLMHF_INJECTED = 0x00000001;
        private const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;

        private const int ProcessCacheLimit = 512;

        private readonly Settings settings;
        private readonly LowLevelMouseProc proc;
        private readonly ConcurrentDictionary<uint, string> processNames = new ConcurrentDictionary<uint, string>();
        private readonly ConcurrentDictionary<uint, byte> pendingLookups = new ConcurrentDictionary<uint, byte>();
        private readonly BlockingCollection<uint> resolverQueue = new BlockingCollection<uint>();
        private readonly Thread resolverThread;

        private static readonly uint currentProcessId = (uint)Environment.ProcessId;

        private IntPtr hookHandle = IntPtr.Zero;
        private volatile bool enabled = true;
        private volatile bool disposed;

        public event Action<int, ScrollAxis>? Wheel;

        public MouseHook(Settings settings)
        {
            this.settings = settings;
            proc = HookCallback; // keep the delegate rooted for the lifetime of the hook

            resolverThread = new Thread(ResolverLoop)
            {
                IsBackground = true,
                Name = "MouseHook.ProcessResolver"
            };
            resolverThread.Start();
        }

        /// <summary>When false, wheel input passes straight through untouched.</summary>
        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public void Install()
        {
            hookHandle = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            if (hookHandle == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Uninstall()
        {
            if (hookHandle == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || !enabled)
                return CallNextHookEx(hookHandle, nCode, wParam, lParam);

            int message = wParam.ToInt32();
            if (message != WM_MOUSEWHEEL && message != WM_MOUSEHWHEEL)
                return CallNextHookEx(hookHandle, nCode, wParam, lParam);

            MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            // Never re-process our own injected steps (would feed back forever).
            if ((data.flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0)
                return CallNextHookEx(hookHandle, nCode, wParam, lParam);

            // Ctrl+wheel is zoom and Alt+wheel belongs to the app: leave those native.
            if (IsKeyDown(VK_CONTROL) || IsKeyDown(VK_MENU))
                return CallNextHookEx(hookHandle, nCode, wParam, lParam);

            uint pid = WindowProbe.GetPidAt(data.pt.x, data.pt.y);
            if (pid == 0 || pid == currentProcessId)
                return CallNextHookEx(hookHandle, nCode, wParam, lParam);

            if (settings.IsBlacklisted(ProcessNameFor(pid)))
                return CallNextHookEx(hookHandle, nCode, wParam, lParam);

            int delta = (short)((data.mouseData >> 16) & 0xFFFF);
            if (delta != 0)
            {
                ScrollAxis axis = message == WM_MOUSEHWHEEL ? ScrollAxis.Horizontal : ScrollAxis.Vertical;
                Wheel?.Invoke(delta, axis);
            }

            return (IntPtr)1; // swallow the physical event; the animator re-sends it
        }

        /// <summary>
        /// Cheap, non-blocking lookup: pid -> cached process name.
        /// Unknown processes are resolved on a worker thread and treated as
        /// "not blacklisted" until the answer arrives.
        /// </summary>
        private string ProcessNameFor(uint pid)
        {
            if (processNames.TryGetValue(pid, out string? cached))
                return cached ?? string.Empty;

            if (pendingLookups.TryAdd(pid, 0) && !disposed)
            {
                try
                {
                    resolverQueue.Add(pid);
                }
                catch (InvalidOperationException)
                {
                    // Queue already completed during shutdown.
                }
            }

            return string.Empty;
        }

        private void ResolverLoop()
        {
            foreach (uint pid in resolverQueue.GetConsumingEnumerable())
            {
                pendingLookups.TryRemove(pid, out _);
                processNames[pid] = WindowProbe.GetProcessName(pid) ?? string.Empty;

                if (processNames.Count > ProcessCacheLimit)
                    processNames.Clear();
            }
        }

        private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            Uninstall();

            try
            {
                resolverQueue.CompleteAdding();
            }
            catch (ObjectDisposedException)
            {
            }

            resolverThread.Join(500);
            resolverQueue.Dispose();
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }
    }
}