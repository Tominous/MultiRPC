﻿using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shell;
using Hardcodet.Wpf.TaskbarNotification;
using MultiRPC.Functions;
using MultiRPC.GUI.Pages;
using MultiRPC.GUI.Views;
using MultiRPC.JsonClasses;
using Path = System.IO.Path;
using ToolTip = MultiRPC.GUI.Controls.ToolTip;

namespace MultiRPC.GUI
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        private static bool firstRun = true;
        private readonly Storyboard _openStoryboard = new Storyboard();
        private Storyboard _closeStoryboard;
        private DateTime _timeWindowWasDeactivated;
        public TaskbarIcon TaskbarIcon;

        protected object ToReturn;
        protected long WindowID; //This is so we can know that the window we look for is the window we are looking for

        public MainWindow()
        {
            InitializeComponent();
            if (this != Application.Current.MainWindow)
                return;

            var mainPage = new MainPage();
            StartLogic(mainPage);
            mainPage.frmContent.Navigated += MainPagefrmContent_OnNavigated;

            TaskbarIcon = new TaskbarIcon
            {
                IconSource = Icon
            };
            TaskbarIcon.TrayLeftMouseDown += IconOnTrayLeftMouseDown;
            TaskbarIcon.TrayToolTip = new ToolTip(App.Text.HideMultiRPC);
        }

        private MainWindow(Page page, bool minButton = true)
        {
            InitializeComponent();
            StartLogic(page);
            ShowInTaskbar = false;
            Title = "MultiRPC - " + page.Title;
            tblTitle.Text = Title;

            if (!minButton)
                btnMin.Visibility = Visibility.Collapsed;
        }

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private void MakeWinAnimation(Storyboard storyboard, double from = 1, double to = 0,
            bool addEventHandler = true, object parameter = null, Duration lengthToRun = new Duration())
        {
            if (!lengthToRun.HasTimeSpan)
                lengthToRun = new Duration(TimeSpan.FromSeconds(0.4));

            if (parameter == null)
                parameter = OpacityProperty;

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = "win" + DateTime.Now.Ticks;
                RegisterName(Name, this);
            }

            var winOpacityAnimation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = lengthToRun,
                EasingFunction = new BackEase()
            };

            storyboard.Children.Add(winOpacityAnimation);
            Storyboard.SetTargetName(winOpacityAnimation, Name);
            Storyboard.SetTargetProperty(winOpacityAnimation, new PropertyPath(parameter));
            if (addEventHandler)
                storyboard.Completed += OpenCloseStoryboard_Completed;
        }

        public static MainWindow GetWindow(long windowID)
        {
            for (var i = 0; i < Application.Current.Windows.Count; i++)
                if (Application.Current.Windows[i] is MainWindow mainWindow && mainWindow.WindowID == windowID)
                    return mainWindow;

            return null;
        }

        private void StartLogic(Page page)
        {
            if (page.MinWidth > Width)
            {
                MinWidth = page.MinWidth;
                Width = MinWidth;
            }

            if (page.MinHeight + 30 > Height)
            {
                MinHeight = page.MinHeight + 30;
                Height = MinHeight;
            }

            MaxHeight = page.MaxHeight;
            MaxWidth = page.MaxWidth;
            frmContent.Content = page;
        }

        private void IconOnTrayLeftMouseDown(object sender, RoutedEventArgs e)
        {
            var timeSpan = DateTime.Now.Subtract(_timeWindowWasDeactivated);
            if (timeSpan.TotalSeconds < 1 || WindowState == WindowState.Minimized)
                WindowState = WindowState == WindowState.Normal ? WindowState.Minimized : WindowState.Normal;

            if (WindowState == WindowState.Normal)
                Activate();
        }

        public static Task<object> OpenWindow(Page page, bool isDialog, long tick, bool minButton)
        {
            var window = new MainWindow(page, minButton)
            {
                WindowID = tick,
                Owner = App.Current.MainWindow,
                ResizeMode = ResizeMode.CanMinimize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            window.Loaded += Window_Loaded;

            if (isDialog) window.ShowDialog();
            else window.Show();

            return Task.FromResult(window.ToReturn);
        }

        private static void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ((MainWindow) sender).Activate();
        }

        public static Task CloseWindow(long windowID, object _return = null)
        {
            for (var i = 0; i < Application.Current.Windows.Count; i++)
                if (Application.Current.Windows[i] is MainWindow mainWindow && mainWindow.WindowID == windowID)
                {
                    mainWindow.ToReturn = _return;
                    mainWindow.ShowInTaskbar = false;
                    mainWindow._closeStoryboard = new Storyboard();
                    mainWindow.MakeWinAnimation(mainWindow._closeStoryboard);
                    mainWindow._closeStoryboard.Begin(mainWindow);
                    break;
                }

            return Task.CompletedTask;
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            if (!File.Exists(App.Config.ActiveTheme))
            {
                App.Config.ActiveTheme = Path.Combine("Assets", "Themes", "DarkTheme" + Theme.ThemeExtension);
                App.Config.Save();
            }

            ThemeEditorPage.UpdateGlobalUI();
            MakeWinAnimation(_openStoryboard, 0, 1);
            _openStoryboard.Begin(this);

            if (firstRun)
            {
                MainPage._MainPage.frmRPCPreview.Content = new RPCPreview(RPCPreview.ViewType.Default);
                firstRun = false;
            }
        }

        public static Task MakeJumpList()
        {
            if (Environment.OSVersion.Version.Major >= 6 && Environment.OSVersion.Version.Minor >= 1)
            {
                var jumpList = new JumpList();

                for (var i = 0; i < 10; i++)
                {
                    if (i > CustomPage.Profiles.Count - 1)
                        break;

                    //Configure a new JumpTask
                    var jumpTask = new JumpTask
                    {
                        // Set the JumpTask properties.
                        ApplicationPath = FileLocations.MultiRPCStartLink,
                        Arguments = $"-custom \"{CustomPage.Profiles.ElementAt(i).Key}\"",
                        IconResourcePath = FileLocations.MultiRPCStartLink,
                        Title = CustomPage.Profiles.ElementAt(i).Key,
                        Description = $"{App.Text.Load} '{CustomPage.Profiles.ElementAt(i).Key}'",
                        CustomCategory = App.Text.CustomProfiles
                    };
                    jumpList.JumpItems.Add(jumpTask);
                }

                JumpList.SetJumpList(Application.Current, jumpList);
            }

            return Task.CompletedTask;
        }

        private void RecHandle_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                ReleaseCapture();
                SendMessage(hwnd, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                hwnd = new IntPtr(0);
            }
        }

        private void Min_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_OnClick(object sender, RoutedEventArgs e)
        {
            ShowInTaskbar = false;
            _closeStoryboard = new Storyboard();
            MakeWinAnimation(_closeStoryboard);
            _closeStoryboard.Begin(this);
        }

        private void OpenCloseStoryboard_Completed(object sender, EventArgs e)
        {
            if (_closeStoryboard != null)
                Close();
        }

        private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Application.Current.MainWindow != this)
                return;

            if (Environment.OSVersion.Version.Major >= 6 && Environment.OSVersion.Version.Minor >= 1)
                TaskbarItemInfo = new TaskbarItemInfo
                {
                    Description = "MultiRPC",
                    ThumbnailClipMargin = new Thickness(ActualWidth - 265, 41, 9, ActualHeight - 126)
                };
        }

        private void Close_OnMouseEnter(object sender, MouseEventArgs e)
        {
            plgCloseIcon.Fill = Brushes.White;
        }

        private void Close_OnMouseLeave(object sender, MouseEventArgs e)
        {
            plgCloseIcon.SetResourceReference(Shape.FillProperty, "TextColourSCBrush");
        }

        private void MainPagefrmContent_OnNavigated(object sender, NavigationEventArgs e)
        {
            if (frmContent.Content != null && Application.Current.MainWindow == this)
            {
                var runCode = true;
                if (RPC.RPCClient != null)
                    if (!RPC.RPCClient.Disposed && RPC.RPCClient.IsInitialized)
                        runCode = false;

                var content = ((Frame) sender).Content;
                if (runCode)
                {
                    switch (content)
                    {
                        case MultiRPCPage _:
                            ((MainPage) frmContent.Content).btnStart.Content = $"{App.Text.Start} MultiRPC";
                            break;
                        case CustomPage _:
                            ((MainPage) frmContent.Content).btnStart.Content = App.Text.StartCustom;
                            break;
                    }
                }
                else
                {
                    ((MainPage) frmContent.Content).btnUpdate.IsEnabled = true;
                    switch (content)
                    {
                        case MultiRPCPage _ when RPC.Type != RPC.RPCType.MultiRPC:
                        case CustomPage _ when RPC.Type != RPC.RPCType.Custom:
                            ((MainPage) frmContent.Content).btnUpdate.IsEnabled = false;
                            break;
                        default:
                        {
                            if (!(content is CustomPage) && !(content is MultiRPCPage))
                                ((MainPage) frmContent.Content).btnUpdate.IsEnabled = false;
                            break;
                        }
                    }
                }
            }
        }

        private void MainWindow_OnActivateChanged(object sender, EventArgs e)
        {
            Animations.ThicknessAnimation(WindowsContent, IsActive ? new Thickness(1) : new Thickness(0),
                WindowsContent.BorderThickness,
                propertyPath: new PropertyPath(Border.BorderThicknessProperty));
            if (TaskbarIcon != null)
                TaskbarIcon.TrayToolTip = new ToolTip(!IsActive ? App.Text.ShowMultiRPC : App.Text.HideMultiRPC);
            if (!IsActive)
                _timeWindowWasDeactivated = DateTime.Now;
        }

        private void MainWindow_OnStateChanged(object sender, EventArgs e)
        {
            Animations.ThicknessAnimation(WindowsContent, WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0),
                WindowsContent.Margin);

            if (Icon != null && App.Config.HideTaskbarIconWhenMin)
                ShowInTaskbar = WindowState != WindowState.Minimized; 
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            if (TaskbarIcon != null)
            {
                TaskbarIcon.Icon = null;
                TaskbarIcon.Dispose();
            }
        }
    }
}