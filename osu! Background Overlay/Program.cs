using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace osu__Background_Overlay
{
    class Program
    {
        /// <summary>
        /// Rough thread delay in milliseconds.
        /// </summary>
        private const int PROCESS_UPDATE_DELAY = 100;
        /// <summary>
        /// Skin files modified by this program will backup previous files
        /// under this name.
        /// </summary>
        private const string SKIN_BACKUP_PREFIX = "BGOBackup_";
        private const string OSU_BACKGROUND_NAME = "menu-background.jpg";

        private static WinApiHelper.RECT ClipRectangle;
        private static Point OsuLocation = new Point(int.MinValue, int.MinValue);
        private static Process OsuProcess;
        private static FileSystemWatcher BackgroundWatcher;

        //Use a global variable so we can lock the file
        private static Bitmap clippedBG = new Bitmap(1, 1);

        private static bool IsShown;

        static void Main()
        {
            //Initialize the system tray
            //Build the menu
            ContextMenu TrayStrip = new ContextMenu();
            MenuItem ShowHideMenuItem = new MenuItem("Show the console");
            MenuItem ExitMenuItem = new MenuItem("Exit o!BGO");
            ShowHideMenuItem.Click += ShowHideMenuItem_Click;
            ExitMenuItem.Click += (sender, e) => Environment.Exit(0);

            TrayStrip.MenuItems.Add(ShowHideMenuItem);
            TrayStrip.MenuItems.Add("-");
            TrayStrip.MenuItems.Add(ExitMenuItem);

            //Build the icon
            NotifyIcon TrayIcon = new NotifyIcon();
            TrayIcon.ContextMenu = TrayStrip;
            TrayIcon.Icon = Properties.Resources.Icon;
            TrayIcon.Text = @"osu! Background Overlay";
            TrayIcon.Visible = true;

            //Application.Run() doesn't return so we move all code to
            //A separate thread
            Thread mainThread = new Thread(ProgramMain)
            {
                IsBackground = true
            };
            mainThread.Start();

            if (Properties.Settings.Default.FirstStart)
            {
                TrayIcon.ShowBalloonTip(5000, "o!BGO", @"o!BGO is running in the system tray. Right-click the icon for options.", ToolTipIcon.Info);
                Properties.Settings.Default.FirstStart = false;
                Properties.Settings.Default.Save();
            }


            //This is required to show the menu
            Application.Run();
        }

        static void ShowHideMenuItem_Click(object sender, EventArgs e)
        {
            if (IsShown)
            {
                WinApiHelper.ShowWindow(WinApiHelper.GetConsoleWindow(), 0);
                ((MenuItem)sender).Text = @"Show the console";
            }
            else
            {
                ((MenuItem)sender).Text = @"Hide the console";
                WinApiHelper.ShowWindow(WinApiHelper.GetConsoleWindow(), 5);
            }
            IsShown = !IsShown;
        }

        private static void ProgramMain()
        {
            //Initially hide the console
            WinApiHelper.ShowWindow(WinApiHelper.GetConsoleWindow(), 0);

            Console.SetWindowSize(60, 10);
            Console.SetBufferSize(60, 500);

            

            BackgroundWatcher = new FileSystemWatcher(Path.GetDirectoryName((string)Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop").GetValue("Wallpaper")));
            BackgroundWatcher.Changed += (sender, e) => WallpaperChanged(1000);
            BackgroundWatcher.EnableRaisingEvents = true;

            Thread monitorThread = new Thread(MonitorOsu)
            {
                IsBackground = true
            };
            monitorThread.Start();

            while (true)
            {
                Console.ReadKey();
            }
        }

        private static void WallpaperChanged(int sleepTime)
        {
            BackgroundWatcher.EnableRaisingEvents = false;
            Thread.Sleep(sleepTime);

            if (OsuProcess != null)
            {
                string osuDirectory = Path.GetDirectoryName(OsuProcess.Modules[0].FileName);
                string currentSkinDirectory = "";
                //Read the user's skin file
                using (StreamReader sR = new StreamReader(Path.Combine(osuDirectory, "osu!." + Environment.UserName + ".cfg")))
                {
                    while (sR.Peek() != -1)
                    {
                        string line = sR.ReadLine();

                        //Check for empty lines
                        if (line.Length == 0)
                            continue;

                        //Check for comment
                        if (line.StartsWith("#"))
                            continue;

                        string lKey = line.Substring(0, line.IndexOf('=')).TrimEnd();
                        string lValue = line.Substring(line.IndexOf('=') + 1).TrimStart();

                        if (lKey == "Skin")
                        {
                            if (lValue == "default")
                            {
                                //Can't do anything with the default skin
                                Log("Can't change the background of the default skin. Please select another skin inside osu!");
                                continue;
                            }
                            currentSkinDirectory = Path.Combine(osuDirectory, @"Skins\" + lValue);
                            break;
                        }
                    }
                }
                if (currentSkinDirectory != "")
                {
                    //Backup the current background
                    //if it exists and hasn't already been backed up
                    if (File.Exists(Path.Combine(currentSkinDirectory, OSU_BACKGROUND_NAME))
                        && !File.Exists(Path.Combine(currentSkinDirectory, SKIN_BACKUP_PREFIX + OSU_BACKGROUND_NAME)))
                    {
                        File.Copy(Path.Combine(currentSkinDirectory, OSU_BACKGROUND_NAME), Path.Combine(currentSkinDirectory, SKIN_BACKUP_PREFIX + OSU_BACKGROUND_NAME));
                    }

                    if (ClipRectangle.Right != 0 && OsuLocation.X != int.MinValue)
                    {
                        lock (clippedBG)
                        {
                            //GC sucks for this type of work
                            //So we'll prematurely dispose the bitmap
                            //to prevent memory leaks

                            using (clippedBG = CaptureFromScreen(OsuLocation.X,
                                OsuLocation.Y,
                                ClipRectangle.Right - ClipRectangle.Left,
                                ClipRectangle.Bottom - ClipRectangle.Top))
                            {
                                //Save to file
                                try
                                {
                                    clippedBG.Save(Path.Combine(currentSkinDirectory, OSU_BACKGROUND_NAME));
                                }
                                catch
                                {
                                    //Generic GDI error, skip frame
                                    return;
                                }
                            }
                        }

                        IntPtr currentWindow = WinApiHelper.GetForegroundWindow();

                        //SendInput sends commands to the foreground window
                        WinApiHelper.SetForegroundWindow(OsuProcess.MainWindowHandle);

                        //Sleep while foreground window is being shown
                        Thread.Sleep(100);

                        WinApiHelper.INPUT[] InputData = new WinApiHelper.INPUT[4];
                        short[] keys = { 0x38, 0x1D, 0x2A, 0x1F };
                        for (int i = 0; i < 4; i++ )
                        {
                            InputData[i].type = 1;
                            InputData[i].ki.wScan = keys[i];
                            InputData[i].ki.dwFlags = 0x0008; //SCANCODE
                        }
                        WinApiHelper.SendInput((uint)InputData.Length, InputData, Marshal.SizeOf(typeof(WinApiHelper.INPUT)));

                        //We don't want to send keyup instantly
                        //100ms delay should be enough in any situation
                        Thread.Sleep(100);

                        for (int i = 0; i < 4; i++)
                            InputData[i].ki.dwFlags |= 0x0002; //KEYUP
                        WinApiHelper.SendInput((uint)InputData.Length, InputData, Marshal.SizeOf(typeof(WinApiHelper.INPUT)));

                        WinApiHelper.SetForegroundWindow(currentWindow);

                        Log("Wallpaper changed!");
                    }
                }
            }
            BackgroundWatcher.EnableRaisingEvents = true;
        }

        private static Bitmap CaptureFromScreen(int x, int y, int width, int height)
        {
            //Pick out the region 
            Bitmap bmp = new Bitmap(width, height);
            Graphics g = Graphics.FromImage(bmp);

            using (Bitmap screenBitmap = GraphicsHelper.CopyScreen())
                g.DrawImage(screenBitmap, new Rectangle(0, 0, width, height), new Rectangle(x, y, width, height), GraphicsUnit.Pixel);

            g.Dispose();
            return bmp;
        }

        private static void MonitorOsu()
        {
            while (true)
            {
                //Find the osu! process if not previously found
                if (OsuProcess == null)
                {
                    Log("Attempting to find the osu! process...");

                    //Normal osu! process
                    Process[] processes = Process.GetProcessesByName("osu!");
                    if (processes.Length == 0)
                    {
                        //We haven't found a normal osu! process
                        //See if the user is using osu! test
                        processes = Process.GetProcessesByName("osu!test");
                    }
                    if (processes.Length == 0)
                    {
                        Log("Failed to find the osu! process.\n" +
                                          "Retrying in 1 second");
                        Thread.Sleep(1000);
                        continue;
                    }
                    OsuProcess = processes[0];

                    Log("Found osu! process!");
                }

                //Temporary point so we can force re-draw if position's changed
                Point p = Point.Empty;

                //Get the window rectangle
                if (!WinApiHelper.GetClientRect(OsuProcess.MainWindowHandle, out ClipRectangle)
                    || !WinApiHelper.ClientToScreen(OsuProcess.MainWindowHandle, out p))
                {
                    //Couldn't find window, reset to null
                    OsuProcess = null;
                }

                if (OsuLocation != p)
                {
                    OsuLocation = p;
                    WallpaperChanged(0);
                }

                Thread.Sleep(PROCESS_UPDATE_DELAY);
            }
        }

        private static void Log(string text)
        {
            Console.WriteLine(@"{0} - {1}", DateTime.Now.ToLongTimeString(), text);
        }
    }
}
