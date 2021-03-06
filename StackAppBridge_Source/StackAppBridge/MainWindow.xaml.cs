﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml.Serialization;
using StackAppBridge.NNTPServer;

namespace StackAppBridge
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : INotifyPropertyChanged
  {
    private static readonly RoutedUICommand _options = new RoutedUICommand("Options", "Options", typeof(MainWindow));
    public static RoutedUICommand Options { get { return _options; } }

    private static readonly RoutedUICommand _infos = new RoutedUICommand("Infos", "Infos", typeof(MainWindow));
    public static RoutedUICommand Info { get { return _infos; } }

    private static readonly RoutedUICommand _sendDebugFiles = new RoutedUICommand("SendDebugFiles", "SendDebugFiles", typeof(MainWindow));
    public static RoutedUICommand SendDebugFiles { get { return _sendDebugFiles; } }
    

    private NNTPServer.NntpServer _nntpServer;
    private DataSourceStackApps _forumsDataSource;
    private bool _started;
    private System.Windows.Forms.NotifyIcon _notifyIcon;

    public bool Started
    {
      get { return _started; }
      set
      {
        _started = value;
        if (_started)
        {
          lblInfo.Text = "Server started.";
          cmdStart.Content = "Stop";
        }
        else
        {
          lblInfo.Text = "Server stopped.";
          cmdStart.Content = "Start";
        }
        RaisePropertyChanged("Started");
      }
    }

    public MainWindow()
    {
      InitializeComponent();
      this.DataContext = this;

      ApplySettings();

      Loaded += MainWindow_Loaded;
    }

    bool _loaded;
    void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
      _loaded = true;

      RemoveCloseCmd();

      InitTrayIcon();

      if (Started == false)
      {
        if (UserSettings.Default.AutoStart)
        {
          StartBridgeAsync(delegate(bool ok, string errorString)
          {
            if (ok)
            {
              if (UserSettings.Default.AutoMinimize)
              {
                WindowState = WindowState.Minimized;
              }
            }
            else
            {
              MessageBox.Show(this, errorString);
            }
          });
        }
      }
    }

    #region TrayIcon

    private void InitTrayIcon()
    {
      // NotifyToSystemTray:
      _notifyIcon = new System.Windows.Forms.NotifyIcon();
      _notifyIcon.BalloonTipText = "The app has been minimized. Click the tray icon to show.";
      _notifyIcon.BalloonTipTitle = UserSettings.ProductNameWithVersion;
      _notifyIcon.Text = UserSettings.ProductNameWithVersion;
      _notifyIcon.Icon = Properties.Resources.StackAppBridge;
      _notifyIcon.Click += NotifyIconClick;

      Closing += OnClose;
      StateChanged += OnStateChanged;
      IsVisibleChanged += OnIsVisibleChanged;
    }

    void OnClose(object sender, System.ComponentModel.CancelEventArgs args)
    {
      _notifyIcon.Dispose();
      _notifyIcon = null;
    }

    private WindowState _storedWindowState = WindowState.Normal;
    void OnStateChanged(object sender, EventArgs args)
    {
      if (WindowState == WindowState.Minimized)
      {
        Hide();
      }
      else
        _storedWindowState = WindowState;
    }
    void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
      CheckTrayIcon();
    }

    void NotifyIconClick(object sender, EventArgs e)
    {
      Show();
      WindowState = _storedWindowState;
    }
    void CheckTrayIcon()
    {
      ShowTrayIcon(!IsVisible);
    }

    void ShowTrayIcon(bool show)
    {
      if (_notifyIcon != null)
        _notifyIcon.Visible = show;
    }

    #endregion

    private void StartBridgeAsync(Action<bool, string> onFinishedCallback)
    {
      int port = 119;
      int parsedPort;
      if (int.TryParse(txtPort.Text, out parsedPort))
        port = parsedPort;

      lblInfo.Text = "Starting server... please wait...";
      cmdStart.IsEnabled = false;

      // First do authetntication in the "main" UI-thread!
      if (UserSettings.Default.UseAuthentication)
      {
        // Try to login....
        Traces.Main_TraceEvent(TraceEventType.Verbose, 1, "OAuth 2.0: Try authentication...");

        var scopes = new List<string>();
        scopes.Add("no_expiry");
        if (UserSettings.Default.ShowInboxAndNotifications)
        {
          scopes.Add("read_inbox");
        }

        var dlg = new AuthWindow(scopes);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true)
        {
          this._accessToken = dlg.AccessToken;
        }
        else
        {
          Traces.Main_TraceEvent(TraceEventType.Error, 1, string.Format(string.Format("Authentication failed: {0}", dlg.ErrorDescription)));
          if (onFinishedCallback != null)
            onFinishedCallback(false,
                             "Could not authenticate with StackExchange! If you want to work without authentication, please disable autehntication in the User Settings (General|UserAuthentication)");
          return;
        }
      }

      var thread = new System.Threading.Thread(
          delegate(object o)
          {
            var t = o as MainWindow;
            var bRes = false;
            var error = string.Empty;
            try
            {
              StartBridgeInternal(t, port);
              bRes = true;
            }
            catch (Exception exp)
            {
              Traces.Main_TraceEvent(TraceEventType.Error, 1, "StartBridgeInternal: {0}", NNTPServer.Traces.ExceptionToString(exp));
              error = exp.Message;
            }
            t.Dispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Normal,
                new Action(
                    delegate
                    {
                      if (bRes)
                      {
                        t.Started = true;
                      }
                      else
                      {
                        t.lblInfo.Text = error;
                        t.ApplySettings();  // for correcting the "LiveId auologin" menu entry
                      }
                      t.cmdStart.IsEnabled = true;
                      if (onFinishedCallback != null)
                        onFinishedCallback(bRes, error);
                    }));
          });

      thread.IsBackground = true;
      thread.SetApartmentState(System.Threading.ApartmentState.STA);
      thread.Start(this);
    }


    private string _accessToken;

    private static void StartBridgeInternal(MainWindow t, int port)
    {
      // Create our DataSource for the forums
      Traces.Main_TraceEvent(TraceEventType.Verbose, 1, "Creating datasource for NNTP server");
      t._forumsDataSource = new DataSourceStackApps(t._accessToken);
      t._forumsDataSource.UsePlainTextConverter = UserSettings.Default.UsePlainTextConverter;
      t._forumsDataSource.AutoLineWrap = UserSettings.Default.AutoLineWrap;
      t._forumsDataSource.HeaderEncoding = UserSettings.Default.EncodingForClientEncoding;
      t._forumsDataSource.InMimeUseHtml = (UserSettings.Default.InMimeUse == UserSettings.MimeContentType.TextHtml);
      t._forumsDataSource.PostsAreAlwaysFormatFlowed = UserSettings.Default.PostsAreAlwaysFormatFlowed;
      t._forumsDataSource.TabAsSpace = UserSettings.Default.TabAsSpace;
      t._forumsDataSource.UseCodeColorizer = UserSettings.Default.UseCodeColorizer;

      t._forumsDataSource.ProgressData += t._forumsDataSource_ProgressData;


      // Now start the NNTP-Server
      Traces.Main_TraceEvent(TraceEventType.Verbose, 1, "Starting NNTP server");
      t._nntpServer = new NNTPServer.NntpServer(t._forumsDataSource, true);
      t._nntpServer.EncodingSend = UserSettings.Default.EncodingForClientEncoding;
      t._nntpServer.ListGroupDisabled = UserSettings.Default.DisableLISTGROUP;
      string errorMessage;
      t._nntpServer.Start(port, 64, UserSettings.Default.BindToWorld, out errorMessage);
      if (errorMessage != null)
      {
        throw new ApplicationException(errorMessage);
      }
    }

    void _forumsDataSource_ProgressData(object sender, ProgressDataEventArgs e)
    {
      Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Normal,
        new Action(() =>
                     {
                       lblInfo.Text = e.Text;
                     }));
    }

    protected override void OnClosed(EventArgs e)
    {
      base.OnClosed(e);
      if (_loaded)
      {
        int parsedPort;
        if (int.TryParse(txtPort.Text, out parsedPort))
          UserSettings.Default.Port = parsedPort;

        UserSettings.Default.Save();
      }
    }

    private void CmdStartClick(object sender, RoutedEventArgs e)
    {
      if (Started)
      {
        _nntpServer.Stop();
        _nntpServer.Dispose();
        _nntpServer = null;
        _forumsDataSource = null;
        //_msdnForumsProviders = null;
        cmdStart.Content = "Start";
        Started = false;
        return;
      }

      StartBridgeAsync(
          delegate(bool ok, string errorString)
          {
            if (ok == false)
            {
              MessageBox.Show(this, errorString);
            }
          });
    }

    private void CbAutoStartChecked(object sender, RoutedEventArgs e)
    {
      if (cbAutoStart.IsChecked.HasValue)
        UserSettings.Default.AutoStart = cbAutoStart.IsChecked.Value;
    }

    private void CbAutoMinimizeChecked(object sender, RoutedEventArgs e)
    {
      if (cbAutoMinimize.IsChecked.HasValue)
        UserSettings.Default.AutoMinimize = cbAutoMinimize.IsChecked.Value;
    }

    private void cbUsePlainTextConverter_Click(object sender, RoutedEventArgs e)
    {
      if (cbUsePlainTextConverter.IsChecked.HasValue)
      {
        UsePlainTextConverters us = UsePlainTextConverters.None;
        if (cbUsePlainTextConverter.IsChecked.Value)
          us = UsePlainTextConverters.SendAndReceive;
        if (_forumsDataSource != null)
        {
          _forumsDataSource.UsePlainTextConverter = us;
        }
        UserSettings.Default.UsePlainTextConverter = us;
      }
    }

    private void OnCloseExecute(object sender, ExecutedRoutedEventArgs e)
    {
      this.Close();
    }

    private void OnCanCloseExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = true;
      e.Handled = true;
    }

    private void OnInfoExecute(object sender, ExecutedRoutedEventArgs e)
    {
      var d = new InfoDialog();
      d.Owner = this;
      d.ShowDialog();
    }

    private void OnCanInfoExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = true;
      e.Handled = true;
    }

    private void OnSendDebugFilesExecute(object sender, ExecutedRoutedEventArgs e)
    {
      var app = (App) Application.Current;
      var dlg = new SendDebugDataWindow();
      dlg.Owner = this;
      if (dlg.ShowDialog() == true)
      {      
        app.SendLogs(null, dlg.UsersEMail, dlg.UsersDescription, dlg.UserSendEmail);
      }
    }

    private void OnCanSendDebugFilesExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = true;
      e.Handled = true;
    }

    private void OnOptionsExecute(object sender, ExecutedRoutedEventArgs e)
    {
      var d = new AdvancedSettingsDialog();
      d.SelectedObject = UserSettings.Default.Clone();
      d.Owner = this;
      if (d.ShowDialog() == true)
      {
        UserSettings.Default = d.SelectedObject as UserSettings;
        ApplySettings(true);
      }
    }

    private void ApplySettings(bool userChange = false)
    {
      Title = UserSettings.ProductNameWithVersion;
      GeneralResponses.SetName(UserSettings.ProductNameWithVersion);
      Article.SetName(UserSettings.ProductNameWithVersion);

      if (userChange)
      {
        var nc = new NewsgroupConfig(UserSettings.Default.Newsgroups);
        nc.Save(Path.Combine(UserSettings.Default.BasePath,
                                                          DataSourceStackApps.NewsgroupConfigFileName));
      }

      cbAutoStart.IsChecked = UserSettings.Default.AutoStart;
      cbAutoMinimize.IsChecked = UserSettings.Default.AutoMinimize;
      if (UserSettings.Default.UsePlainTextConverter == UsePlainTextConverters.None)
        cbUsePlainTextConverter.IsChecked = false;
      else if (UserSettings.Default.UsePlainTextConverter == UsePlainTextConverters.SendAndReceive)
        cbUsePlainTextConverter.IsChecked = true;
      else
        cbUsePlainTextConverter.IsChecked = null;

      txtPort.Text = UserSettings.Default.Port.ToString();

      if (_forumsDataSource != null)
      {
        _forumsDataSource.ClearCache();
        _forumsDataSource.UsePlainTextConverter = UserSettings.Default.UsePlainTextConverter;
        _forumsDataSource.AutoLineWrap = UserSettings.Default.AutoLineWrap;
        _forumsDataSource.HeaderEncoding = UserSettings.Default.EncodingForClientEncoding;
        _forumsDataSource.InMimeUseHtml = (UserSettings.Default.InMimeUse == UserSettings.MimeContentType.TextHtml);
        _forumsDataSource.PostsAreAlwaysFormatFlowed = UserSettings.Default.PostsAreAlwaysFormatFlowed;
        _forumsDataSource.TabAsSpace = UserSettings.Default.TabAsSpace;
        _forumsDataSource.UseCodeColorizer = UserSettings.Default.UseCodeColorizer;
      }

      if (_nntpServer != null)
      {
        _nntpServer.EncodingSend = UserSettings.Default.EncodingForClientEncoding;
        _nntpServer.ListGroupDisabled = UserSettings.Default.DisableLISTGROUP;
      }
    }

    private void OnCanOptionsExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      e.CanExecute = true;
      e.Handled = true;
    }

    private DebugWindow _debugWindow;
    private void mnuDebugWindow_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        if (mnuDebugWindow.IsChecked)
        {
          _debugWindow = new DebugWindow();
          _debugWindow.Owner = this;
          _debugWindow.Show();
          _debugWindow.Closed += new EventHandler(_debugWindow_Closed);
        }
        else
        {
          _debugWindow.Close();
          _debugWindow = null;
        }
      }
      catch (Exception exp)
      {
        Traces.Main_TraceEvent(TraceEventType.Error, 1, "Error in mnuDebugWindow_click: {0}", NNTPServer.Traces.ExceptionToString(exp));
      }
    }

    void _debugWindow_Closed(object sender, EventArgs e)
    {
      mnuDebugWindow.IsChecked = false;
    }

    private void cmdExit_Click(object sender, RoutedEventArgs e)
    {
      Close();
    }

    #region Win32
    //private const int GWL_STYLE = -16;
    //private const int WS_SYSMENU = 0x80000;
    //[DllImport("user32.dll", SetLastError = true)]
    //private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    //[DllImport("user32.dll")]
    //private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    //const int MF_BYPOSITION = 0x400;
    private const int MF_BYCOMMAND = 0x0;
    private const int SC_CLOSE = 0xF060;
    [DllImport("User32")]
    private static extern int RemoveMenu(IntPtr hMenu, int nPosition, int wFlags);
    [DllImport("User32")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
    [DllImport("User32")]
    private static extern int GetMenuItemCount(IntPtr hWnd);

    void RemoveCloseCmd()
    {
      var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
      IntPtr hMenu = GetSystemMenu(hwnd, false);
      int menuItemCount = GetMenuItemCount(hMenu);
      //RemoveMenu(hMenu, menuItemCount - 1, MF_BYPOSITION);
      RemoveMenu(hMenu, SC_CLOSE, MF_BYCOMMAND);
    }
    #endregion

    public event PropertyChangedEventHandler PropertyChanged;
    void RaisePropertyChanged(string name)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(name));
    }
  }  // class MainWindow
}
