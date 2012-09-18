﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using EasyConnect.Properties;
using EasyConnect.Protocols;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Taskbar;
using Stratman.Windows.Forms.TitleBarTabs;
using Win32Interop.Enums;
using Win32Interop.Methods;
using wyDay.Controls;

namespace EasyConnect
{
    /// <summary>
    /// Main application form for EasyConnect that hosts the tabs in which the various windows are displayed.
    /// </summary>
    public partial class MainForm : TitleBarTabs
    {
        /// <summary>
        /// Delegate to open the <see cref="HistoryWindow.HistoricalConnection"/> whose <see cref="IConnection.Guid"/> matches <paramref name="historyGuid"/>.
        /// </summary>
        /// <param name="historyGuid">Value to use to match against <see cref="IConnection.Guid"/> when searching for a 
        /// <see cref="HistoryWindow.HistoricalConnection"/></param>
        /// <returns>The newly created tab for the connection to the <see cref="HistoryWindow.HistoricalConnection"/> whose <see cref="IConnection.Guid"/>
        /// matches <paramref name="historyGuid"/>.</returns>
        public delegate TitleBarTab ConnectToHistoryDelegate(Guid historyGuid);

        /// <summary>
        /// Instance of the first created MainForm, used for remoting purposes to open history entries via the jump list.
        /// </summary>
        public static MainForm ActiveInstance = null;

        /// <summary>
        /// Remoting method to use to open history entries via the jump list.
        /// </summary>
        public static ConnectToHistoryDelegate ConnectToHistoryMethod = null;

        /// <summary>
        /// When the application is invoked via the jump list to open a historical connection, we use this IPC channel to communicate with an already-open 
        /// process (if it exists) to tell it to open the given historical connection.
        /// </summary>
        protected static IpcServerChannel _ipcChannel = null;

        /// <summary>
        /// Flag that indicates if we're in the process of creating a new tab.
        /// </summary>
        protected bool _addingWindow = false;

        /// <summary>
        /// Controls the automatic updating process.
        /// </summary>
        protected AutomaticUpdater _automaticUpdater;

        /// <summary>
        /// Contains the UI for using bookmarks but also the data concerning all of the bookmarks and folders that the user has.
        /// </summary>
        protected BookmarksWindow _bookmarks = null;

        /// <summary>
        /// Flag indicating whether or not the user is currently pressing the Ctrl key.
        /// </summary>
        protected bool _ctrlDown = false;

        /// <summary>
        /// Contains the UI for looking at a user's connection history but also the data concerning those historical connections.
        /// </summary>
        protected HistoryWindow _history = null;

        /// <summary>
        /// Pointer to the low-level mouse hook callback (<see cref="KeyboardHookCallback"/>).
        /// </summary>
        protected IntPtr _hookId;

        /// <summary>
        /// Delegate of <see cref="KeyboardHookCallback"/>; declared as a member variable to keep it from being garbage collected.
        /// </summary>
        protected HOOKPROC _hookproc = null;

        /// <summary>
        /// Jump list for this application.
        /// </summary>
        protected JumpList _jumpList = null;

        /// <summary>
        /// Application-level (not connection protocol defaults) that the user has set.
        /// </summary>
        protected Options _options;

        /// <summary>
        /// The preview images for each tab used to display each tab when Aero Peek is activated.
        /// </summary>
        protected Dictionary<Form, Bitmap> _previews = new Dictionary<Form, Bitmap>();

        /// <summary>
        /// When switching between tabs, this keeps track of the tab that was previously active so that, when it is switched away from, we can generate a
        /// fresh Aero Peek preview image for it.
        /// </summary>
        protected TitleBarTab _previousActiveTab = null;

        /// <summary>
        /// When populating the application's jump list with the recent connections that the user has made, this is the category that the items go under.
        /// </summary>
        protected JumpListCustomCategory _recentCategory = new JumpListCustomCategory("Recent");

        /// <summary>
        /// Queue of the ten most recent connections, indicating which ones are going to show up in the jump list.
        /// </summary>
        protected Queue<HistoryWindow.HistoricalConnection> _recentConnections = new Queue<HistoryWindow.HistoricalConnection>();

        /// <summary>
        /// Flag indicating whether the user is pressing the Shift button.
        /// </summary>
        protected bool _shiftDown = false;

        /// <summary>
        /// Flag indicating whether <see cref="_automaticUpdater"/> has told us that there's an update available for the application.
        /// </summary>
        private bool _updateAvailable;

        /// <summary>
        /// Encryption type that was previously selected by the user
        /// </summary>
        protected EncryptionType _previousEncryptionType;

        /// <summary>
        /// Constructor; initializes the UI, creates the <see cref="_automaticUpdater"/>, loads the bookmark and history data, and sets up the IPC remoting
        /// channel and low-level keyboard hook.
        /// </summary>
        /// <param name="openToBookmarks">Bookmarks, if any, that we should open when initially creating the UI.</param>
        public MainForm(Guid[] openToBookmarks)
        {
            InitializeComponent();

            // Create the automatic updater control
            //_automaticUpdater = new AutomaticUpdater();

            //(_automaticUpdater as ISupportInitialize).BeginInit();
            //_automaticUpdater.ContainerForm = this;
            //_automaticUpdater.Name = "_automaticUpdater";
            //_automaticUpdater.TabIndex = 0;
            //_automaticUpdater.wyUpdateCommandline = null;
            //_automaticUpdater.Visible = false;
            //_automaticUpdater.KeepHidden = true;
            //_automaticUpdater.GUID = "752f8ae7-47f3-4299-adcc-8be32d63ec7a";
            //_automaticUpdater.DaysBetweenChecks = 2;
            //_automaticUpdater.UpdateType = UpdateType.Automatic;
            //_automaticUpdater.ReadyToBeInstalled += _automaticUpdater_ReadyToBeInstalled;
            //_automaticUpdater.UpToDate += _automaticUpdater_UpToDate;
            //_automaticUpdater.CheckingFailed += _automaticUpdater_CheckingFailed;
            //(_automaticUpdater as ISupportInitialize).EndInit();

            //Controls.Add(_automaticUpdater);

            OpenToBookmarks = openToBookmarks;
            bool convertingToRsa = false;

            // If the user hasn't formally selected an encryption type (either they're starting the application for the first time or are running a legacy
            // version that explicitly used Rijndael), ask them if they want to use RSA
            if (Options.EncryptionType == null)
            {
                string messageBoxText = @"Do you want to use an RSA key container to encrypt your passwords?

The RSA encryption mode uses cryptographic keys associated with 
your Windows user account to encrypt sensitive data without having 
to enter an encryption password every time you start this 
application. However, your bookmarks file will be tied uniquely to 
this user account and you will be unable to share them between
multiple users.";

                if (!Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\EasyConnect"))
                    messageBoxText += @"

The alternative is to derive an encryption key from a password that
you will need to enter every time that this application starts.";

                else
                    messageBoxText += @"

Since you've already encrypted your data with a password once, 
you would need to enter it one more time to decrypt it before RSA 
can be used.";

                Options.EncryptionType = MessageBox.Show(messageBoxText, "Use RSA?", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes
                                             ? EncryptionType.Rsa
                                             : EncryptionType.Rijndael;

                // Since they want to use RSA but already have connection data encrypted with Rijndael, we'll have to capture that password so that we can
                // decrypt it using Rijndael and then re-encrypt it using the RSA keypair
                convertingToRsa = Options.EncryptionType == EncryptionType.Rsa &&
                                  Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\EasyConnect");
            }

            // If this is the first time that the user is running the application, pop up and information box informing them that they're going to enter a
            // password used to encrypt sensitive connection details
            if (!Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\EasyConnect"))
            {
                if (Options.EncryptionType == EncryptionType.Rijndael)
                    MessageBox.Show(Resources.FirstRunPasswordText, Resources.FirstRunPasswordTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

                Directory.CreateDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\EasyConnect");
            }

            if (Options.EncryptionType != null)
                Options.Save();

            bool encryptionTypeSet = false;

            while (Bookmarks == null || _history == null)
            {
                // Get the user's encryption password via the password dialog
                if (!encryptionTypeSet && (Options.EncryptionType == EncryptionType.Rijndael || convertingToRsa))
                {
                    PasswordWindow passwordWindow = new PasswordWindow();
                    passwordWindow.ShowDialog();

                    ConnectionFactory.SetEncryptionType(EncryptionType.Rijndael, passwordWindow.Password);
                }

                else
                    ConnectionFactory.SetEncryptionType(Options.EncryptionType.Value);

                // Create the bookmark and history windows which will try to use the password to decrypt sensitive connection details; if it's unable to, an
                // exception will be thrown that wraps a CryptographicException instance
                try
                {
                    _bookmarks = new BookmarksWindow(this);
                    _history = new HistoryWindow(this);

                    ConnectionFactory.GetDefaultProtocol();

                    encryptionTypeSet = true;
                }

                catch (Exception e)
                {
                    if ((Options.EncryptionType == EncryptionType.Rijndael || convertingToRsa) && !ContainsCryptographicException(e))
                        throw;

                    // Tell the user that their password is incorrect and, if they click OK, repeat the process
                    DialogResult result = MessageBox.Show(
                        Resources.IncorrectPasswordText, Resources.ErrorTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Error);

                    if (result != DialogResult.OK)
                    {
                        Closing = true;
                        return;
                    }
                }
            }

            // If we're converting over to RSA, we've already loaded and decrypted the sensitive data using 
            if (convertingToRsa)
                SetEncryptionType(Options.EncryptionType.Value, null);

            // Create a remoting channel used to tell this window to open historical connections when entries in the jump list are clicked
            if (_ipcChannel == null)
            {
                _ipcChannel = new IpcServerChannel("EasyConnect");
                ChannelServices.RegisterChannel(_ipcChannel, false);
                RemotingConfiguration.RegisterWellKnownServiceType(typeof (HistoryMethods), "HistoryMethods", WellKnownObjectMode.SingleCall);
            }

            // Wire up the tab event handlers
            TabSelected += MainForm_TabSelected;
            TabDeselecting += MainForm_TabDeselecting;
            TabClicked += MainForm_TabClicked;

            ActiveInstance = this;
            ConnectToHistoryMethod = ConnectToHistory;

            TabRenderer = new ChromeTabRenderer(this);

            // Show the bookmarks manager window initially if we're not opening bookmarks or history entries
            if (OpenToBookmarks == null && OpenToHistory == Guid.Empty)
                OpenBookmarkManager();

            // Get the low-level keyboard hook that will be used to process shortcut keys
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hookproc = KeyboardHookCallback;
                _hookId = User32.SetWindowsHookEx(WH.WH_KEYBOARD_LL, _hookproc, Kernel32.GetModuleHandleW(curModule.ModuleName), 0);
            }
        }

        private void SetEncryptionType(EncryptionType encryptionType, SecureString encryptionPassword)
        {
            ConnectionFactory.SetEncryptionType(encryptionType, encryptionPassword);

            _bookmarks.Save();
            _history.Save();
            ConnectionFactory.SetDefaults(ConnectionFactory.GetDefaults(ConnectionFactory.GetDefaultProtocol()));
        }

        /// <summary>
        /// Controls the automatic updating process.
        /// </summary>
        public AutomaticUpdater AutomaticUpdater
        {
            get
            {
                return _automaticUpdater;
            }
        }

        /// <summary>
        /// Application-level (not connection protocol defaults) that the user has set.
        /// </summary>
        public Options Options
        {
            get
            {
                return _options ?? (_options = Options.Load());
            }
        }

        /// <summary>
        /// Flag indicating whether <see cref="_automaticUpdater"/> has told us that there's an update available for the application.
        /// </summary>
        public bool UpdateAvailable
        {
            get
            {
                return _updateAvailable;
            }

            set
            {
                _updateAvailable = value;

                // Go through each ConnectionWindow that's open and set the update available state, which controls whether the "update available" icon shows
                // up on the tools toolbar icon
                foreach (TitleBarTab tab in Tabs.Where(tab => tab.Content is ConnectionWindow))
                    (tab.Content as ConnectionWindow).SetUpdateAvailableState(_updateAvailable);
            }
        }

        /// <summary>
        /// Contains the UI for using bookmarks but also the data concerning all of the bookmarks and folders that the user has.
        /// </summary>
        public BookmarksWindow Bookmarks
        {
            get
            {
                if (_bookmarks == null && ConnectionFactory.ReadyForCrypto)
                    _bookmarks = new BookmarksWindow(this);

                return _bookmarks;
            }
        }

        /// <summary>
        /// Contains the UI for looking at a user's connection history but also the data concerning those historical connections.
        /// </summary>
        public HistoryWindow History
        {
            get
            {
                if (_history == null && ConnectionFactory.ReadyForCrypto)
                    _history = new HistoryWindow(this);

                return _history;
            }
        }

        /// <summary>
        /// Flag indicating whether we are in the process of closing the window.
        /// </summary>
        public new bool Closing
        {
            get;
            set;
        }

        /// <summary>
        /// <see cref="IConnection.Guid"/> of the <see cref="HistoryWindow.HistoricalConnection"/> that we should connect to initially.
        /// </summary>
        public Guid OpenToHistory
        {
            get;
            set;
        }

        /// <summary>
        /// Bookmarks, if any, that we should open when initially creating the UI.
        /// </summary>
        public Guid[] OpenToBookmarks
        {
            get;
            set;
        }

        /// <summary>
        /// Processes shortcut keys.
        /// </summary>
        /// <param name="nCode">Code indicating if we should process this message.</param>
        /// <param name="wParam"><see cref="WM"/> enumeration value which is the message that's being passed to us.</param>
        /// <param name="lParam">Virtual key code of the key being pressed.</param>
        /// <returns>The result of the next hook in the queue (<see cref="User32.CallNextHookEx"/>).</returns>
        protected IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Only process the key press if our window is active
            if (nCode >= 0 && User32.GetActiveWindow() == Handle)
            {
                // Get the key that was pressed
                Key key = KeyInterop.KeyFromVirtualKey(Marshal.ReadInt32(lParam));

                switch ((WM) wParam.ToInt32())
                {
                    case WM.WM_KEYDOWN:
                    case WM.WM_SYSKEYDOWN:
                        switch (key)
                        {
                            case Key.RightCtrl:
                            case Key.LeftCtrl:
                                _ctrlDown = true;
                                break;

                            case Key.RightShift:
                            case Key.LeftShift:
                                _shiftDown = true;
                                break;

                            // Ctrl+T creates a new tab
                            case Key.T:
                                if (_ctrlDown)
                                    AddNewTab();

                                break;

                            // Ctrl+Tab cycles forward in the tab list, while Ctrl+Shift+Tab cycles backward
                            case Key.Tab:
                                if (Tabs.Count > 1)
                                {
                                    if (_ctrlDown && _shiftDown)
                                    {
                                        if (SelectedTabIndex == 0)
                                            SelectedTabIndex = Tabs.Count - 1;

                                        else
                                            SelectedTabIndex--;
                                    }

                                    else if (_ctrlDown)
                                    {
                                        if (SelectedTabIndex == Tabs.Count - 1)
                                            SelectedTabIndex = 0;

                                        else
                                            SelectedTabIndex++;
                                    }
                                }

                                break;
                        }

                        break;

                    case WM.WM_KEYUP:
                    case WM.WM_SYSKEYUP:
                        switch (key)
                        {
                            case Key.RightCtrl:
                            case Key.LeftCtrl:
                                _ctrlDown = false;
                                break;

                            case Key.RightShift:
                            case Key.LeftShift:
                                _shiftDown = false;
                                break;
                        }

                        break;
                }
            }

            // Call the next hook in the queue
            return User32.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Recursive method that checks to see if <see cref="exception"/> or any of its <see cref="Exception.InnerException"/>s wrap a 
        /// <see cref="CryptographicException"/> instance.
        /// </summary>
        /// <param name="exception">Exception that we're currently examining.</param>
        /// <returns>True if <paramref name="exception"/> or any of its <see cref="Exception.InnerException"/>s wrap a <see cref="CryptographicException"/> 
        /// instance, false otherwise.</returns>
        protected bool ContainsCryptographicException(Exception exception)
        {
            if (exception is CryptographicException)
                return true;

            return exception.InnerException != null && ContainsCryptographicException(exception.InnerException);
        }

        /// <summary>
        /// Handler method that's called when a tab is clicked on.  This is different from the <see cref="MainForm_TabSelected"/> event handler in that this is
        /// called even if the tab is currently active.  This is used to show the toolbar for <see cref="ConnectionWindow"/> instances that automatically hide
        /// their toolbars when the connection's UI is focused on.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        private void MainForm_TabClicked(object sender, TitleBarTabEventArgs e)
        {
            // Only show the toolbar if the user clicked on an already-selected tab
            if (e.Tab.Content is ConnectionWindow && e.Tab == SelectedTab)
                (e.Tab.Content as ConnectionWindow).ShowToolbar();
        }

        ///// <summary>
        ///// Handler method that's called when <see cref="_automaticUpdater"/> fails during update checking.  Sets <see cref="UpdateAvailable"/> to false.
        ///// </summary>
        ///// <param name="sender">Object from which this event originated, <see cref="_automaticUpdater"/> in this case.</param>
        ///// <param name="e">Details about the failure that occurred.</param>
        //private void _automaticUpdater_CheckingFailed(object sender, FailArgs e)
        //{
        //    UpdateAvailable = false;
        //}

        ///// <summary>
        ///// Handler method that's called when <see cref="_automaticUpdater"/> finds that the application is already up-to-date.  Sets 
        ///// <see cref="UpdateAvailable"/> to false.
        ///// </summary>
        ///// <param name="sender">Object from which this event originated, <see cref="_automaticUpdater"/> in this case.</param>
        ///// <param name="e">Arguments associated with this event.</param>
        //private void _automaticUpdater_UpToDate(object sender, SuccessArgs e)
        //{
        //    UpdateAvailable = false;
        //}

        /// <summary>
        /// Called from <see cref="ConnectionWindow"/> instances when the user chooses to install an available update either from the update checking window
        /// or from the tools menu.
        /// </summary>
        public void InstallUpdate()
        {
            if (UpdateAvailable)
                _automaticUpdater.InstallNow();
        }

        ///// <summary>
        ///// Handler method that's called when <see cref="_automaticUpdater"/> finds an update available for the application.  Sets 
        ///// <see cref="UpdateAvailable"/> to true.
        ///// </summary>
        ///// <param name="sender">Object from which this event originated, <see cref="_automaticUpdater"/> in this case.</param>
        ///// <param name="e">Arguments associated with this event.</param>
        //private void _automaticUpdater_ReadyToBeInstalled(object sender, EventArgs e)
        //{
        //    UpdateAvailable = true;
        //}

        /// <summary>
        /// Handler method that's called when the user closes the <see cref="BookmarksWindow"/> tab.  Sets <see cref="_bookmarks"/> to null so that we know we
        /// need to create a new instance the next time the user tries to open it.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        protected void Bookmarks_FormClosed(object sender, FormClosedEventArgs e)
        {
            _bookmarks = null;
        }

        /// <summary>
        /// Opens an <see cref="OptionsWindow"/> instance when the user clicks on the "Options" menu item from a <see cref="ConnectionWindow"/>.
        /// </summary>
        public void OpenOptions()
        {
            TitleBarTab tab = Tabs.FirstOrDefault(t => t.Content is OptionsWindow);

            // Focus on the options tab if a window is already open
            if (tab != null)
            {
                SelectedTab = tab;
                return;
            }

            _previousEncryptionType = Options.EncryptionType ?? EncryptionType.Rijndael;

            // Create the options window and then add entries for each protocol type to the window
            OptionsWindow optionsWindow = new OptionsWindow(this);
            GlobalOptionsWindow globalOptionsWindow = new GlobalOptionsWindow();

            globalOptionsWindow.Closed += globalOptionsWindow_Closed;
            optionsWindow.OptionsForms.Add(globalOptionsWindow);

            foreach (IProtocol protocol in ConnectionFactory.GetProtocols())
            {
                Form optionsForm = protocol.GetOptionsFormInDefaultsMode();

                optionsForm.Closed += (sender, args) => ConnectionFactory.SetDefaults(((IOptionsForm) optionsForm).Connection);
                optionsWindow.OptionsForms.Add(optionsForm);
            }

            ShowInEmptyTab(optionsWindow);
        }

        void globalOptionsWindow_Closed(object sender, EventArgs e)
        {
            if (_previousEncryptionType != Options.EncryptionType)
                // ReSharper disable PossibleInvalidOperationException
                SetEncryptionType(Options.EncryptionType.Value, (sender as GlobalOptionsWindow).EncryptionPassword);
                // ReSharper restore PossibleInvalidOperationException
        }

        /// <summary>
        /// Creates a new tab to hold <paramref name="form"/>.
        /// </summary>
        /// <param name="form">Form instance that we are to open in a new tab.</param>
        protected void ShowInEmptyTab(Form form)
        {
            // If we're opening the form from an unconnected ConnectionWindow, just replace its content with the new form
            if (SelectedTab != null && SelectedTab.Content is ConnectionWindow && !(SelectedTab.Content as ConnectionWindow).IsConnected)
            {
                Form oldWindow = SelectedTab.Content;

                SelectedTab.Content = form;
                ResizeTabContents();

                oldWindow.Close();
            }

            // Otherwise, create a new tab associated with the form
            else
            {
                TitleBarTab newTab = new TitleBarTab(this)
                    {
                        Content = form
                    };

                Tabs.Add(newTab);
                ResizeTabContents(newTab);
                SelectedTabIndex = _tabs.Count - 1;
            }

            form.Show();

            if (_overlay != null)
                _overlay.Render(true);
        }

        /// <summary>
        /// Opens a <see cref="HistoryWindow"/> instance when the user clicks on the "History" menu item in the tools menu from a 
        /// <see cref="ConnectionWindow"/> instance.
        /// </summary>
        public void OpenHistory()
        {
            TitleBarTab tab = Tabs.FirstOrDefault(t => t.Content is HistoryWindow);

            // Focus on the existing history window tab if it exists
            if (tab != null)
            {
                SelectedTab = tab;
                return;
            }

            History.FormClosed += History_FormClosed;
            ShowInEmptyTab(History);
        }

        /// <summary>
        /// Handler method that's called when the user closes the <see cref="HistoryWindow"/> tab.  Sets <see cref="_history"/> to null so that we know we
        /// need to create a new instance the next time the user tries to open it.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        private void History_FormClosed(object sender, FormClosedEventArgs e)
        {
            _history = null;
        }

        /// <summary>
        /// Opens a <see cref="BookmarksWindow"/> instance when the user clicks on the "Bookmarks" menu item in the tools menu from a 
        /// <see cref="ConnectionWindow"/> instance.
        /// </summary>
        public void OpenBookmarkManager()
        {
            TitleBarTab tab = Tabs.FirstOrDefault(t => t.Content is BookmarksWindow);

            // Focus on the existing bookmarks manager tab if it exists
            if (tab != null)
            {
                SelectedTab = tab;
                return;
            }

            Bookmarks.FormClosed += Bookmarks_FormClosed;
            ShowInEmptyTab(Bookmarks);
        }

        /// <summary>
        /// Handler method that's called when a <see cref="TitleBarTab"/> is in the process of losing focus.  Grabs an image of the tab's content to be used
        /// when Aero Peek is activated.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        protected void MainForm_TabDeselecting(object sender, TitleBarTabCancelEventArgs e)
        {
            if (_previousActiveTab == null)
                return;

            TabbedThumbnail preview = TaskbarManager.Instance.TabbedThumbnail.GetThumbnailPreview(_previousActiveTab.Content);

            if (preview == null)
                return;

            Bitmap bitmap = TabbedThumbnailScreenCapture.GrabWindowBitmap(_previousActiveTab.Content.Handle, _previousActiveTab.Content.Size);

            preview.SetImage(bitmap);

            // If we already had a preview image for the tab, dispose of it
            if (_previews.ContainsKey(_previousActiveTab.Content))
                _previews[_previousActiveTab.Content].Dispose();

            _previews[_previousActiveTab.Content] = bitmap;
        }

        /// <summary>
        /// Handler method that's called when a <see cref="TitleBarTab"/> gains focus.  Sets the active window in Aero Peek via a call to 
        /// <see cref="TabbedThumbnailManager.SetActiveTab(Control)"/>.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        protected void MainForm_TabSelected(object sender, TitleBarTabEventArgs e)
        {
            if (!_addingWindow && SelectedTabIndex != -1 && _previews.ContainsKey(SelectedTab.Content))
                TaskbarManager.Instance.TabbedThumbnail.SetActiveTab(SelectedTab.Content);

            _previousActiveTab = SelectedTab;
        }

        /// <summary>
        /// Opens the <see cref="HistoryWindow.HistoricalConnection"/> whose <see cref="IConnection.Guid"/> property matches <paramref name="historyGuid"/>.
        /// </summary>
        /// <param name="historyGuid">GUID used to identify the <see cref="HistoryWindow.HistoricalConnection"/> to open.</param>
        /// <returns><see cref="ConnectionWindow"/> tab for the <see cref="HistoryWindow.HistoricalConnection"/> whose <see cref="IConnection.Guid"/> property 
        /// matches <paramref name="historyGuid"/>.</returns>
        public TitleBarTab ConnectToHistory(Guid historyGuid)
        {
            IConnection connection = _history.FindInHistory(historyGuid);

            if (connection != null)
                return Connect(connection);

            return null;
        }

        /// <summary>
        /// Opens the <see cref="IConnection"/>s whose <see cref="IConnection.Guid"/> property matches items in <paramref name="bookmarkGuids"/>.
        /// </summary>
        /// <param name="bookmarkGuids">GUIDs used to identify the <see cref="IConnection"/>s to open.</param>
        public void ConnectToBookmarks(Guid[] bookmarkGuids)
        {
            foreach (Guid bookmarkGuid in bookmarkGuids)
                Connect(_bookmarks.FindBookmark(bookmarkGuid));

            SelectedTabIndex = Tabs.Count - 1;
        }

        /// <summary>
        /// Opens a new tab for <paramref name="connection"/>.
        /// </summary>
        /// <param name="connection">Connection that we are to open a new tab for.</param>
        /// <returns>Tab that was created for <paramref name="connection"/>.</returns>
        public TitleBarTab Connect(IConnection connection)
        {
            return Connect(connection, false);
        }

        /// <summary>
        /// Opens a new tab for <paramref name="connection"/>.
        /// </summary>
        /// <param name="connection">Connection that we are to open a new tab for.</param>
        /// <param name="focusNewTab">Flag indicating whether we should focus on the new tab.</param>
        /// <returns>Tab that was created for <paramref name="connection"/>.</returns>
        public TitleBarTab Connect(IConnection connection, bool focusNewTab)
        {
            ConnectionWindow connectionWindow = new ConnectionWindow(connection);

            _addingWindow = true;
            TitleBarTab newTab = new TitleBarTab(this)
                {
                    Content = connectionWindow
                };
            Tabs.Insert(SelectedTabIndex + 1, newTab);
            ResizeTabContents(newTab);
            _addingWindow = false;

            if (focusNewTab)
                SelectedTab = newTab;

            connectionWindow.Connect();

            return newTab;
        }

        /// <summary>
        /// Called from <see cref="ConnectionWindow"/> instances when a connection is established.  Creates a thumbnail preview for the tab (if one doesn't
        /// exist already) for Aero Peek and adds the connection to the list of entries on the application's jump list.
        /// </summary>
        /// <param name="connectionWindow"></param>
        /// <param name="connection"></param>
        public void RegisterConnection(ConnectionWindow connectionWindow, IConnection connection)
        {
            _history.AddToHistory(connection);

            // Create the Aero Peek preview instance
            if (!_previews.ContainsKey(connectionWindow))
            {
                connectionWindow.FormClosing += ConnectionWindow_FormClosing;

                TabbedThumbnail preview = new TabbedThumbnail(Handle, connectionWindow)
                    {
                        Title = connectionWindow.Text,
                        Tooltip = connectionWindow.Text
                    };

                preview.SetWindowIcon(connectionWindow.Icon);
                preview.TabbedThumbnailActivated += preview_TabbedThumbnailActivated;
                preview.TabbedThumbnailClosed += preview_TabbedThumbnailClosed;
                preview.TabbedThumbnailBitmapRequested += preview_TabbedThumbnailBitmapRequested;
                preview.PeekOffset = new Vector(connectionWindow.Location.X, connectionWindow.Location.Y);

                for (Control currentControl = connectionWindow.Parent; currentControl.Parent != null; currentControl = currentControl.Parent)
                    preview.PeekOffset += new Vector(currentControl.Location.X, currentControl.Location.Y);

                TaskbarManager.Instance.TabbedThumbnail.AddThumbnailPreview(preview);
                TaskbarManager.Instance.TabbedThumbnail.SetActiveTab(preview);
            }

            // Add the connection to the jump list
            if (_recentConnections.FirstOrDefault((HistoryWindow.HistoricalConnection c) => c.Connection.Guid == connection.Guid) == null)
            {
                _recentCategory.AddJumpListItems(
                    new JumpListLink(Application.ExecutablePath, connectionWindow.Text)
                        {
                            Arguments = "/openHistory:" + connection.Guid.ToString(),
                            IconReference =
                                new IconReference(Application.ExecutablePath, 0)
                        });
                _jumpList.Refresh();

                _recentConnections.Enqueue(_history.Connections.First((HistoryWindow.HistoricalConnection c) => c.Connection.Guid == connection.Guid));

                if (_recentConnections.Count > _jumpList.MaxSlotsInList)
                    _recentConnections.Dequeue();
            }
        }

        /// <summary>
        /// Handler method that's called when Aero Peek needs to display a thumbnail for a <see cref="ConnectionWindow"/>; finds the preview bitmap generated
        /// in <see cref="MainForm_TabDeselecting"/> and returns that.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        private void preview_TabbedThumbnailBitmapRequested(object sender, TabbedThumbnailBitmapRequestedEventArgs e)
        {
            foreach (
                TitleBarTab rdcWindow in
                    Tabs.Where(tab => tab.Content is IConnectionForm).Where(
                        rdcWindow => rdcWindow.Content.Handle == e.WindowHandle && _previews.ContainsKey(rdcWindow.Content)))
            {
                e.SetImage(_previews[rdcWindow.Content]);
                break;
            }
        }

        /// <summary>
        /// Handler method that's called when a <see cref="ConnectionWindow"/> instance is closed.  Cleans up data associated with the window's Aero Peek
        /// instance.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        private void ConnectionWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            Form window = (Form) sender;

            if (_previews.ContainsKey(window))
            {
                _previews[window].Dispose();
                _previews.Remove(window);
            }

            if (_previousActiveTab != null && window == _previousActiveTab.Content)
                _previousActiveTab = null;

            if (!window.IsDisposed)
                TaskbarManager.Instance.TabbedThumbnail.RemoveThumbnailPreview(window);
        }

        /// <summary>
        /// Handler method that's called when the user clicks the close button in an Aero Peek preview thumbnail.  Finds the window associated with the
        /// thumbnail and calls <see cref="Form.Close"/> on it.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        private void preview_TabbedThumbnailClosed(object sender, TabbedThumbnailEventArgs e)
        {
            foreach (TitleBarTab tab in Tabs.Where(tab => tab.Content.Handle == e.WindowHandle))
            {
                tab.Content.Close();
                TaskbarManager.Instance.TabbedThumbnail.RemoveThumbnailPreview(e.TabbedThumbnail);

                break;
            }
        }

        /// <summary>
        /// Handler method that's called when the user clicks on an Aero Peek preview thumbnail.  Finds the tab associated with the thumbnail and focuses on
        /// it.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        private void preview_TabbedThumbnailActivated(object sender, TabbedThumbnailEventArgs e)
        {
            foreach (TitleBarTab tab in Tabs.Where(tab => tab.Content.Handle == e.WindowHandle))
            {
                SelectedTabIndex = Tabs.IndexOf(tab);

                TaskbarManager.Instance.TabbedThumbnail.SetActiveTab(tab.Content);
                break;
            }

            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
        }

        /// <summary>
        /// Handler method that's called when the form is closing; saves the bookmarks.
        /// </summary>
        /// <param name="sender">Object from which this event originated.</param>
        /// <param name="e">Arguments associated with this event.</param>
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_bookmarks != null)
                _bookmarks.Save();
        }

        /// <summary>
        /// Handler method that's called when the form is shown.  Creates and initializes the jump list if necessary and, if they are specified, opens the
        /// bookmarks specified by <see cref="OpenToBookmarks"/> or the history entries pointed to by <see cref="OpenToHistory"/>.
        /// </summary>
        /// <param name="e">Arguments associated with this event.</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_jumpList == null)
            {
                _jumpList = JumpList.CreateJumpList();
                _jumpList.KnownCategoryToDisplay = JumpListKnownCategoryType.Neither;
                _jumpList.AddCustomCategories(_recentCategory);

                // Get all of the historical connections and order them by their last connection times
                List<HistoryWindow.HistoricalConnection> historicalConnections =
                    _history.Connections.OrderBy((HistoryWindow.HistoricalConnection c) => c.LastConnection).ToList();
                historicalConnections = historicalConnections.GetRange(0, Math.Min(historicalConnections.Count, Convert.ToInt32(_jumpList.MaxSlotsInList)));

                // Add each history entry to the jump list
                foreach (HistoryWindow.HistoricalConnection historicalConnection in historicalConnections)
                {
                    _recentCategory.AddJumpListItems(
                        new JumpListLink(Application.ExecutablePath, historicalConnection.Connection.DisplayName)
                            {
                                Arguments = "/openHistory:" + historicalConnection.Connection.Guid.ToString(),
                                IconReference = new IconReference(Application.ExecutablePath, 0)
                            });
                    _recentConnections.Enqueue(historicalConnection);
                }

                _jumpList.Refresh();

                if (OpenToHistory != Guid.Empty)
                    SelectedTab = Connect(_history.FindInHistory(OpenToHistory));
            }

            if (OpenToHistory == Guid.Empty && OpenToBookmarks != null)
                ConnectToBookmarks(OpenToBookmarks);
        }

        /// <summary>
        /// Method to create a new tab when the add button in the title bar is clicked; creates a new <see cref="ConnectionWindow"/>.
        /// </summary>
        /// <returns>Tab for a new <see cref="ConnectionWindow"/> instance.</returns>
        public override TitleBarTab CreateTab()
        {
            return new TitleBarTab(this)
                {
                    Content = new ConnectionWindow()
                };
        }

        /// <summary>
        /// Custom message pump method for the window.  Processes the <see cref="WM.WM_MOUSEACTIVATE"/> message.
        /// </summary>
        /// <param name="m">Message that we are to process.</param>
        protected override void WndProc(ref Message m)
        {
            switch ((WM)m.Msg)
            {
                // If the selected tab is a connection window and the cursor is over the content area of the window, focus on that content
                case WM.WM_MOUSEACTIVATE:
                    if (SelectedTab != null && SelectedTab.Content is ConnectionWindow)
                    {
                        if ((SelectedTab.Content as ConnectionWindow).IsCursorOverContent)
                            (SelectedTab.Content as ConnectionWindow).FocusContent();
                    }

                    base.WndProc(ref m);
                    break;

                default:
                    base.WndProc(ref m);
                    break;
            }
        }

        /// <summary>
        /// Initiates an update check for the application.
        /// </summary>
        /// <returns>True if the update process was started successfully, false otherwise.</returns>
        public bool CheckForUpdate()
        {
            return _automaticUpdater.ForceCheckForUpdate(true);
        }
    }
}