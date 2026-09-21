using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AuraApp
{
    static class Program
    {
        // ---------- Win32 API ----------

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW",
            CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(
            uint uAction, uint uParam, string lpvParam, uint fuWinIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW",
            CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(
            uint uAction, uint uParam, StringBuilder lpvParam, uint fuWinIni);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern void SHChangeNotify(
            uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetShortPathName(
            string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOZORDER   = 0x0004;
        private const int SW_HIDE         = 0;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // ---------- Константы ----------

        private const uint SPI_SETDESKWALLPAPER = 20;
        private const uint SPI_GETDESKWALLPAPER = 0x0073;
        private const uint SPIF_UPDATEINIFILE   = 0x01;
        private const uint SPIF_SENDCHANGE      = 0x02;

        private const uint SHCNE_ASSOCCHANGED   = 0x08000000;
        private const uint SHCNF_IDLIST         = 0x0000;

        // Максимум одновременно открытых окон-ошибок (лишние закрываются автоматически)
        private const int MAX_ERROR_DIALOGS = 4;

        // Текст первой ошибки и троллфейс-старт
        private const string FirstErrorMessage = "ну всё пк 200";
        private const long FirstErrorDurationMs = 3000;

        // ---------- Состояние ----------

        static List<Form> activeWindows = new List<Form>();
        static readonly object lockObj = new object();

        static string originalWallpaper = "";
        static string originalWallpaperStyle = "2";
        static string originalTileWallpaper = "0";

        static List<string> desktopDirs = new List<string>();

        static bool cleanedUp = false;
        static readonly object cleanupLock = new object();

        static Stream GetResource(string name)
        {
            return typeof(Program).Assembly.GetManifestResourceStream(name);
        }

        static void ExtractResource(string resourceName, string targetPath)
        {
            try
            {
                using (Stream s = GetResource(resourceName))
                {
                    if (s == null) return;
                    using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buf = new byte[65536];
                        int read;
                        while ((read = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, read);
                        }
                    }
                }
            }
            catch {}
        }

        static Image LoadResourceImage(string resourceName, string fallbackPath)
        {
            try
            {
                using (Stream s = GetResource(resourceName))
                {
                    if (s != null)
                    {
                        using (Image temp = Image.FromStream(s))
                        {
                            return new Bitmap(temp);
                        }
                    }
                }
            }
            catch {}

            if (!string.IsNullOrEmpty(fallbackPath) && File.Exists(fallbackPath))
            {
                try
                {
                    using (Image temp = Image.FromFile(fallbackPath))
                    {
                        return new Bitmap(temp);
                    }
                }
                catch {}
            }

            return null;
        }

        private static void SaveOriginalWallpaperSettings()
        {
            try
            {
                StringBuilder sb = new StringBuilder(260);
                if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
                {
                    originalWallpaper = sb.ToString();
                }

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", false))
                {
                    if (key != null)
                    {
                        object style = key.GetValue("WallpaperStyle");
                        object tile  = key.GetValue("TileWallpaper");
                        if (style != null) originalWallpaperStyle = style.ToString();
                        if (tile  != null) originalTileWallpaper  = tile.ToString();
                    }
                }
            }
            catch {}
        }

        public static bool SetWallpaper(string imagePath, string wallpaperStyle = "2", string tile = "0")
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                {
                    if (key != null)
                    {
                        key.SetValue("WallpaperStyle", wallpaperStyle);
                        key.SetValue("TileWallpaper", tile);
                    }
                }

                string path = imagePath;
                if (path.IndexOfAny(new[] { ' ', '\t' }) >= 0 || ContainsNonAscii(path))
                {
                    StringBuilder shortPath = new StringBuilder(260);
                    if (GetShortPathName(path, shortPath, (uint)shortPath.Capacity) > 0)
                        path = shortPath.ToString();
                }

                bool ok = SystemParametersInfo(
                    SPI_SETDESKWALLPAPER, 0, path,
                    SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                return ok;
            }
            catch
            {
                return false;
            }
        }

        public static void RestoreOriginalWallpaper()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                {
                    if (key != null)
                    {
                        key.SetValue("WallpaperStyle", originalWallpaperStyle);
                        key.SetValue("TileWallpaper", originalTileWallpaper);
                    }
                }

                if (!string.IsNullOrEmpty(originalWallpaper))
                {
                    SystemParametersInfo(
                        SPI_SETDESKWALLPAPER, 0, originalWallpaper,
                        SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                }
                else
                {
                    SystemParametersInfo(
                        SPI_SETDESKWALLPAPER, 0, "",
                        SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                }

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch {}
        }

        private static bool ContainsNonAscii(string s)
        {
            foreach (char c in s)
                if (c > 127) return true;
            return false;
        }

        static void EnforceDialogLimit()
        {
            lock (lockObj)
            {
                // Оставляем место для нового окна: закрываем самые старые
                while (activeWindows.Count >= MAX_ERROR_DIALOGS)
                {
                    Form oldest = activeWindows[0];
                    activeWindows.RemoveAt(0);
                    CloseFormSafe(oldest);
                }
            }
        }

        static void CloseFormSafe(Form f)
        {
            try
            {
                if (f.IsDisposed) return;
                if (f.InvokeRequired)
                    f.BeginInvoke(new Action(() => { try { f.Close(); } catch {} }));
                else
                    f.Close();
            }
            catch {}
        }

        static void SpawnErrorDialog(string lyricsLine, int x, int y)
        {
            SpawnErrorDialog(lyricsLine, x, y, false, false);
        }

        static void SpawnErrorDialog(string lyricsLine, int x, int y, bool isFirstError, bool center)
        {
            EnforceDialogLimit();

            Thread t = new Thread(() =>
            {
                try
                {
                    Form errForm = new Form();
                    errForm.Text = "AURA.exe - Fatal Error";
                    errForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                    errForm.MaximizeBox = false;
                    errForm.MinimizeBox = false;
                    errForm.Location = new Point(x, y);
                    errForm.Size = new Size(430, 165);
                    errForm.TopMost = true;
                    errForm.ShowIcon = true;

                    if (center)
                    {
                        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                        errForm.StartPosition = FormStartPosition.Manual;
                        errForm.Location = new Point(
                            wa.Left + (wa.Width - errForm.Width) / 2,
                            wa.Top + (wa.Height - errForm.Height) / 2);
                    }
                    else
                    {
                        errForm.StartPosition = FormStartPosition.Manual;
                    }

                    PictureBox iconBox = new PictureBox();
                    iconBox.Image = SystemIcons.Error.ToBitmap();
                    iconBox.Location = new Point(20, 25);
                    iconBox.Size = new Size(32, 32);
                    iconBox.SizeMode = PictureBoxSizeMode.StretchImage;
                    errForm.Controls.Add(iconBox);

                    Label lbl = new Label();
                    lbl.Text = lyricsLine;
                    lbl.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
                    lbl.ForeColor = Color.Red;
                    lbl.Location = new Point(65, 20);
                    lbl.Size = new Size(340, 55);
                    errForm.Controls.Add(lbl);

                    Button btn = new Button();
                    btn.Text = "OK";
                    btn.Location = new Point(170, 85);
                    btn.Size = new Size(90, 30);
                    btn.Click += (s, e) => errForm.Close();
                    errForm.Controls.Add(btn);

                    // Timer to keep error window above fullscreen overlay
                    System.Windows.Forms.Timer topTimer = new System.Windows.Forms.Timer();
                    topTimer.Interval = 200;
                    topTimer.Tick += (s, e) =>
                    {
                        if (!errForm.IsDisposed && errForm.IsHandleCreated)
                        {
                            SetWindowPos(errForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        }
                    };

                    errForm.Load += (s, e) => topTimer.Start();
                    errForm.FormClosed += (s, e) => topTimer.Stop();

                    lock (lockObj)
                    {
                        activeWindows.Add(errForm);
                    }

                    errForm.FormClosed += (s, e) =>
                    {
                        lock (lockObj)
                        {
                            activeWindows.Remove(errForm);
                        }
                    };

                    try { SystemSounds.Hand.Play(); } catch {}

                    if (isFirstError)
                    {
                        // Первое окно "ну всё пк 200" живёт ровно 3 секунды и само закрывается
                        System.Windows.Forms.Timer lifeTimer = new System.Windows.Forms.Timer();
                        lifeTimer.Interval = (int)FirstErrorDurationMs;
                        lifeTimer.Tick += (s, e) =>
                        {
                            lifeTimer.Stop();
                            try { errForm.Close(); } catch {}
                        };
                        errForm.Load += (s, e) => lifeTimer.Start();
                        errForm.FormClosed += (s, e) => { lifeTimer.Stop(); lifeTimer.Dispose(); };
                    }

                    Application.Run(errForm);
                }
                catch {}
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        static void ShowFullscreenJumpscare(List<Image> images)
        {
            if (images == null || images.Count == 0) return;

            Thread t = new Thread(() =>
            {
                try
                {
                    Form fsForm = new Form();
                    fsForm.FormBorderStyle = FormBorderStyle.None;
                    fsForm.WindowState = FormWindowState.Normal;
                    fsForm.StartPosition = FormStartPosition.Manual;
                    fsForm.Bounds = Screen.PrimaryScreen.Bounds; // True fullscreen coverage
                    fsForm.TopMost = true;
                    fsForm.BackColor = Color.Black;
                    fsForm.Cursor = Cursors.WaitCursor;

                    PictureBox pb = new PictureBox();
                    pb.Dock = DockStyle.Fill;
                    pb.SizeMode = PictureBoxSizeMode.Zoom;
                    pb.BackColor = Color.Black;
                    pb.Image = images[0];
                    fsForm.Controls.Add(pb);

                    // Во время скримера панель задач скрыта и прижата к низу z-order
                    lock (lockObj)
                    {
                        activeWindows.Add(fsForm);
                    }

                    fsForm.Load += (s, e) => { taskbarLockedByForms = true; if (!isLiteMode) LockTaskbar(); };

                    System.Windows.Forms.Timer cycleTimer = new System.Windows.Forms.Timer();
                    cycleTimer.Interval = 250;
                    int curIdx = 0;
                    cycleTimer.Tick += (s, e) =>
                    {
                        if (images.Count > 1)
                        {
                            curIdx = (curIdx + 1) % images.Count;
                            pb.Image = images[curIdx];
                            pb.Invalidate();
                            pb.Update();
                        }
                    };
                    cycleTimer.Start();

                    fsForm.FormClosed += (s, e) =>
                    {
                        try
                        {
                            cycleTimer.Stop();
                            cycleTimer.Dispose();
                        }
                        catch {}

                        lock (lockObj)
                        {
                            activeWindows.Remove(fsForm);
                        }

                        taskbarLockedByForms = false;
                        RestoreTaskbar();
                    };

                    Application.Run(fsForm);
                }
                catch {}
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        public static void Cleanup()
        {
            lock (cleanupLock)
            {
                if (cleanedUp) return;
                cleanedUp = true;
            }

            try
            {
                if (!isLiteMode)
                {
                    RestoreOriginalWallpaper();
                }

                // NOTE: User requested NOT to delete .txt files from desktop!
                // So .txt files remain on the desktop.

                // Возвращаем панель задач (в конце пранка)
                taskbarLockedByForms = false;
                if (!isLiteMode)
                {
                    RestoreTaskbar();
                }

                // Закрываем активные окна
                lock (lockObj)
                {
                    foreach (Form f in activeWindows)
                    {
                        try
                        {
                            if (f.InvokeRequired)
                                f.Invoke(new Action(() => f.Close()));
                            else
                                f.Close();
                        }
                        catch {}
                    }
                    activeWindows.Clear();
                }

                string tempDir = Path.GetTempPath();
                try { File.Delete(Path.Combine(tempDir, "aura_play.vbs")); } catch {}
                try { File.Delete(Path.Combine(tempDir, "aura_music.mp3")); } catch {}
                try { File.Delete(Path.Combine(tempDir, "aura_wallpaper.png")); } catch {}

                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch {}
        }

        static bool isLiteMode = false;

        // ---------- Блокировка панели задач ----------

        static bool taskbarLocked = false;
        static bool taskbarLockedByForms = false;
        static Thread taskbarThread = null;

        static readonly string[] TaskbarClasses = new string[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        static void LockTaskbar()
        {
            taskbarLocked = true;
            if (taskbarThread != null) return;

            taskbarThread = new Thread(() =>
            {
                while (taskbarLocked)
                {
                    try
                    {
                        foreach (string cls in TaskbarClasses)
                        {
                            IntPtr h = FindWindow(cls, null);
                            if (h != IntPtr.Zero)
                            {
                                ShowWindow(h, SW_HIDE);
                                SetWindowPos(h, HWND_BOTTOM, 0, 0, 0, 0,
                                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                            }
                        }
                    }
                    catch {}
                    Thread.Sleep(120);
                }
                taskbarThread = null;
            });
            taskbarThread.IsBackground = true;
            taskbarThread.Start();
        }

        static void RestoreTaskbar()
        {
            if (taskbarLockedByForms) return;
            taskbarLocked = false;
            try
            {
                foreach (string cls in TaskbarClasses)
                {
                    IntPtr h = FindWindow(cls, null);
                    if (h != IntPtr.Zero)
                    {
                        ShowWindow(h, 5 /* SW_SHOW */);
                        SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                }
            }
            catch {}
        }

        [STAThread]
        static void Main(string[] args)
        {
#if LITE
            isLiteMode = true;
#endif
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (string.Equals(a, "--lite", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "-lite", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "/lite", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "--safe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "-safe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "/safe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, "--no-desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        isLiteMode = true;
                    }
                }
            }

            try
            {
                string procName = Process.GetCurrentProcess().ProcessName.ToLowerInvariant();
                if (procName.Contains("lite") || procName.Contains("safe") || procName.Contains("clean"))
                {
                    isLiteMode = true;
                }
            }
            catch {}

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!isLiteMode)
            {
                SaveOriginalWallpaperSettings();
                // Блокируем панель задач на время пранка (снимется в Cleanup)
                LockTaskbar();
            }

            Application.ApplicationExit += (s, e) => Cleanup();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Cleanup();

            try
            {
                RunPrank();
            }
            finally
            {
                Cleanup();
            }
        }

        static void RunPrank()
        {
            string tempDir = Path.GetTempPath();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloadsDir = Path.Combine(userProfile, "Downloads");
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string assetsDir = Path.Combine(appDir, "assets");

            string oneDriveDesktop = Path.Combine(userProfile, @"OneDrive\Desktop");
            if (Directory.Exists(oneDriveDesktop))
                desktopDirs.Add(oneDriveDesktop);

            string localDesktop = Path.Combine(userProfile, "Desktop");
            if (Directory.Exists(localDesktop) && !desktopDirs.Contains(localDesktop))
                desktopDirs.Add(localDesktop);

            if (desktopDirs.Count == 0)
                desktopDirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

            string extractedMusic = Path.Combine(tempDir, "aura_music.mp3");
            string extractedWallpaper = Path.Combine(tempDir, "aura_wallpaper.png");

            ExtractResource("music.mp3", extractedMusic);
            if (!isLiteMode)
            {
                ExtractResource("wallpaper.png", extractedWallpaper);
            }

            string musicPath = File.Exists(extractedMusic) ? extractedMusic : Path.Combine(downloadsDir, "TIKI TIKI.mp3");
            if (!File.Exists(musicPath) && File.Exists(Path.Combine(assetsDir, "music.mp3")))
                musicPath = Path.Combine(assetsDir, "music.mp3");

            string wallpaperPath = File.Exists(extractedWallpaper) ? extractedWallpaper : Path.Combine(downloadsDir, "1394671.png");
            if (!File.Exists(wallpaperPath) && File.Exists(Path.Combine(assetsDir, "wallpaper.png")))
                wallpaperPath = Path.Combine(assetsDir, "wallpaper.png");

            List<Image> trollImages = new List<Image>();
            for (int i = 1; i <= 3; i++)
            {
                string pPng = i + ".png";
                string pJpg = i + ".jpg";

                Image img = LoadResourceImage(pPng, Path.Combine(downloadsDir, pPng));
                if (img == null) img = LoadResourceImage(pJpg, Path.Combine(downloadsDir, pJpg));
                if (img == null) img = LoadResourceImage(pPng, Path.Combine(assetsDir, pPng));
                if (img == null) img = LoadResourceImage(pJpg, Path.Combine(assetsDir, pJpg));
                if (img == null) img = LoadResourceImage(pPng, Path.Combine(appDir, pPng));
                if (img == null) img = LoadResourceImage(pJpg, Path.Combine(appDir, pJpg));

                if (img != null)
                {
                    trollImages.Add(img);
                }
            }

            // Установка обоев (только в полной версии)
            if (!isLiteMode && File.Exists(wallpaperPath))
            {
                SetWallpaper(wallpaperPath);
            }

            // Воспроизведение музыки
            string vbsPath = Path.Combine(tempDir, "aura_play.vbs");
            Process musicProcess = null;

            if (File.Exists(musicPath))
            {
                try
                {
                    string vbsCode = 
                        "Set w = CreateObject(\"WMPlayer.OCX\")\r\n" +
                        "w.settings.volume = 100\r\n" +
                        "w.URL = \"" + musicPath.Replace("\\", "\\\\") + "\"\r\n" +
                        "w.controls.play\r\n" +
                        "While w.playState = 0 Or w.playState = 9 Or w.playState = 6\r\n" +
                        "  WScript.Sleep 200\r\n" +
                        "Wend\r\n" +
                        "While w.playState = 3\r\n" +
                        "  WScript.Sleep 500\r\n" +
                        "Wend\r\n";

                    File.WriteAllText(vbsPath, vbsCode, Encoding.ASCII);

                    ProcessStartInfo psi = new ProcessStartInfo("wscript.exe", "\"" + vbsPath + "\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    musicProcess = Process.Start(psi);
                }
                catch {}
            }

            // Заполнение рабочего стола файлами (только в полной версии)
            if (!isLiteMode)
            {
                StringBuilder sb = new StringBuilder();
                for (int line = 0; line < 10000; line++)
                {
                    sb.AppendLine("AURA");
                }
                string auraContent = sb.ToString();

                const int totalFiles = 280; // Полное заполнение сетки рабочего стола (1920x1080 / 2K)
                for (int i = 0; i < totalFiles; i++)
                {
                    string fname = (i == 0) ? "AURA.txt" : string.Format("AURA ({0}).txt", i);
                    foreach (string dt in desktopDirs)
                    {
                        string fpath = Path.Combine(dt, fname);
                        try
                        {
                            File.WriteAllText(fpath, auraContent, Encoding.UTF8);
                        }
                        catch {}
                    }
                    if (i % 20 == 0)
                    {
                        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                    }
                    Thread.Sleep(5);
                }
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }

            // Текст песни для окон ошибок
            string[] lyrics = new string[]
            {
                "Tiki, tiki, tiki, mate teki takata.",
                "Tiki, tiki, tiki, tiki, tiki.",
                "Tiki, tiki, tiki, tiki, tiki takata.",
                "Tiki, tiki, tiki, mate teki takata.",
                "Tiki, tiki, tiki, mate teki takata.",
                "Tiki, tiki, tiki, mate teki takata.",
                "Tiki, tiki, tiki, tiki, tiki takata, takata, takata.",
                "Tiki, tiki, tiki, mate teki takata.",
                "Tiki, tiki, tiki, tiki, tiki takata."
            };

            Random rnd = new Random();
            int screenW = Screen.PrimaryScreen.Bounds.Width;
            int screenH = Screen.PrimaryScreen.Bounds.Height;

            Stopwatch sw = Stopwatch.StartNew();
            int lyricsIdx = 0;
            long lastPopupTime = 0;

            // 1) Первая ошибка "ну всё пк 200" — ровно 3 секунды, по центру экрана
            SpawnErrorDialog(FirstErrorMessage, 0, 0, true, true);

            long firstErrorStarted = 0;
            while (sw.ElapsedMilliseconds < FirstErrorDurationMs)
            {
                Thread.Sleep(50);
                firstErrorStarted = sw.ElapsedMilliseconds;
            }

            // 2) Сразу после неё — полноэкранный троллфейс
            if (trollImages.Count > 0)
            {
                ShowFullscreenJumpscare(trollImages);
            }

            // 3) И только потом начинается каскад окон с текстом песни
            lastPopupTime = firstErrorStarted;

            while (true)
            {
                Thread.Sleep(250);

                long elapsed = sw.ElapsedMilliseconds;

                if (musicProcess != null && musicProcess.HasExited)
                {
                    break;
                }
                if (elapsed >= 160000)
                {
                    break;
                }

                // Появление окон с текстом песни (поверх всего)
                if (elapsed - lastPopupTime > 1500)
                {
                    lastPopupTime = elapsed;
                    string curLine = lyrics[lyricsIdx % lyrics.Length];
                    lyricsIdx++;

                    int posX = rnd.Next(30, Math.Max(40, screenW - 460));
                    int posY = rnd.Next(30, Math.Max(40, screenH - 240));

                    SpawnErrorDialog(curLine, posX, posY);

                    if (lyricsIdx % 2 == 0)
                    {
                        int posX2 = rnd.Next(30, Math.Max(40, screenW - 460));
                        int posY2 = rnd.Next(30, Math.Max(40, screenH - 240));
                        SpawnErrorDialog(curLine, posX2, posY2);
                    }
                }
            }
        }
    }
}
