﻿
using System;
using System.Windows.Forms;

using Microsoft.Lync.Model;
using Microsoft.Lync.Model.Conversation;
using System.Text;

using ThingM.Blink1;
using ThingM.Blink1.ColorProcessor;
using System.Management;
using System.Diagnostics;
using System.Threading;
using System.Drawing;
using Uctrl.Arduino;

namespace LyncPresenceBridge
{
	class StateColor
	{
		private static Icon colorToIcon(int red, int green, int blue)
		{
			using (Bitmap b = Bitmap.FromHicon(new Icon(Properties.Resources.TrayIcon, 48, 48).Handle))
			{
				Graphics g = Graphics.FromImage(b);
				g.FillRegion(new SolidBrush(Color.FromArgb(red, green, blue)), new Region(new Rectangle(11, 5, 26, 24)));
				g.FillRegion(new SolidBrush(Color.FromArgb(100, red, green, blue)), new Region(new Rectangle(10, 4, 28, 26)));

				IntPtr Hicon = b.GetHicon();
				Icon newIcon = Icon.FromHandle(Hicon);
				return newIcon;
			}
		}

		public StateColor(string[] colorString)
		{
			arduinoColor = Array.ConvertAll(colorString, s => Convert.ToByte(s));
			blinkColor = new Rgb(Convert.ToByte(colorString[0]), Convert.ToByte(colorString[1]), Convert.ToByte(colorString[2]));
			trayIcon = colorToIcon(blinkColor.Red, blinkColor.Green, blinkColor.Blue);
		}

		public byte[] arduinoColor;
		public Rgb blinkColor;
		public Icon trayIcon;
	};

    class LyncConnectorAppContext : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayIconContextMenu;

        private LyncClient lyncClient;
        private Blink1 blink1 = new Blink1();

        private Arduino arduino = new Arduino();

        private ManagementEventWatcher usbWatcher;

		private System.Timers.Timer stateCheckTimer;

        private bool isLyncIntegratedMode = true;

		// Define State Colors
		private StateColor STATE_COLOR_AVAILABLE = new StateColor(Properties.Settings.Default.ColorAvailable.Split(','));
		private StateColor STATE_COLOR_AVAILABLE_IDLE = new StateColor(Properties.Settings.Default.ColorAvailableIdle.Split(','));
		private StateColor STATE_COLOR_BUSY = new StateColor(Properties.Settings.Default.ColorBusy.Split(','));
		private StateColor STATE_COLOR_BUSY_IDLE = new StateColor(Properties.Settings.Default.ColorBusyIdle.Split(','));
		private StateColor STATE_COLOR_AWAY = new StateColor(Properties.Settings.Default.ColorAway.Split(','));
		private StateColor STATE_COLOR_DO_NOT_DISTURB = new StateColor(Properties.Settings.Default.ColorDoNotDisturb.Split(','));
		private StateColor STATE_COLOR_OFF = new StateColor(Properties.Settings.Default.ColorOff.Split(','));

		public LyncConnectorAppContext()
        {
            Application.ApplicationExit += new EventHandler(OnApplicationExit);
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnApplicationExit);

            // Setup UI, NotifyIcon
            InitializeComponent();

            trayIcon.Visible = true;

            // Setup Blink
            InitializeBlink1();

            // Open Arduino serial port it's not 0
            if (Properties.Settings.Default.ArduinoSerialPort > 0)
            {
                if (! arduino.OpenPort("COM" + Properties.Settings.Default.ArduinoSerialPort.ToString()))
                {
                    trayIcon.ShowBalloonTip(1000, "Error", "Could not open and init serial port.", ToolTipIcon.Warning);
                }
            }

            // Setup Lync Client Connection
            GetLyncClient();

            // Watch for USB Changes, try to monitor blink plugin/removal
            InitializeUSBWatcher();

			stateCheckTimer = new System.Timers.Timer(15000);
			stateCheckTimer.Elapsed += OnTimedStateCheckEvent;
			stateCheckTimer.AutoReset = true;
			stateCheckTimer.Enabled = true;
		}

		private bool InitializeBlink1()
        {
            try
            {
                blink1.Open();
            }
            catch (InvalidOperationException iox)
            {
                // No blink devices attached, switching to loacl mode (in the future) 
                Debug.WriteLine(iox.ToString());
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());
            }

            return blink1.IsConnected;

        }

        private void InitializeComponent()
        {
            trayIcon = new NotifyIcon();

            // The icon is added to the project resources.
            trayIcon.Icon = Properties.Resources.TrayIcon;

            // TrayIconContextMenu
            trayIconContextMenu = new ContextMenuStrip();
            trayIconContextMenu.SuspendLayout();
            trayIconContextMenu.Name = "TrayIconContextMenu";

            // Tray Context Menuitems to set color
            this.trayIconContextMenu.Items.Add("Available", null, new EventHandler(AvailableMenuItem_Click));
            this.trayIconContextMenu.Items.Add("Busy", null, new EventHandler(BusyMenuItem_Click));
            this.trayIconContextMenu.Items.Add("Away", null, new EventHandler(AwayMenuItem_Click));
            this.trayIconContextMenu.Items.Add("DoNotDisturb", null, new EventHandler(DoNotDisturbMenuItem_Click));
            this.trayIconContextMenu.Items.Add("Off", null, new EventHandler(OffMenuItem_Click));

            // Separation Line
            this.trayIconContextMenu.Items.Add(new ToolStripSeparator());

            // About Form Line
            this.trayIconContextMenu.Items.Add("About", null, new EventHandler(aboutMenuItem_Click));

            // Settings Form Line
            this.trayIconContextMenu.Items.Add("Settings", null, new EventHandler(settingsMenuItem_Click));

            // Separation Line
            this.trayIconContextMenu.Items.Add(new ToolStripSeparator());

            // CloseMenuItem
            this.trayIconContextMenu.Items.Add("Exit", null, new EventHandler(CloseMenuItem_Click));


            trayIconContextMenu.ResumeLayout(false);
            trayIcon.ContextMenuStrip = trayIconContextMenu;
        }

        private void GetLyncClient()
        {
            try
            {
                // try to get the running lync client and register for change events, if Client is not running then ClientNoFound Exception is thrown by lync api
                lyncClient = LyncClient.GetClient();
                lyncClient.StateChanged += lyncClient_StateChanged;
                
                if (lyncClient.State == ClientState.SignedIn)
                {
                    lyncClient.Self.Contact.ContactInformationChanged += Contact_ContactInformationChanged;
                    lyncClient.ConversationManager.ConversationAdded += ConversationManager_ConversationAdded;
                    lyncClient.ConversationManager.ConversationRemoved += ConversationManager_ConversationRemoved;
                }

                SetCurrentContactState();
				SetLyncIntegrationMode(true);
			}
			catch (ClientNotFoundException)
            {
                Debug.WriteLine("Lync Client not started.");

                SetLyncIntegrationMode(false);

                trayIcon.ShowBalloonTip(1000, "Error", "Lync Client not started. Running in manual mode now. Please use the context menu to change your blink color.", ToolTipIcon.Warning);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.ToString());

                trayIcon.ShowBalloonTip(1000, "Error", "Something went wrong by getting your Lync status. Running in manual mode now. Please use the context menu to change your blink color.", ToolTipIcon.Warning);
                Debug.WriteLine(e.Message);
            }
        }

        void SetLyncIntegrationMode(bool isLyncIntegrated)
        {
            isLyncIntegratedMode = isLyncIntegrated;
        }

        /// <summary>
        /// Read the current Availability Information from Lync/Skype for Business and set the color 
        /// </summary>
        void SetCurrentContactState()
        {
            if (lyncClient.State == ClientState.SignedIn)
            {
                ContactAvailability currentAvailability = (ContactAvailability)lyncClient.Self.Contact.GetContactInformation(ContactInformationType.Availability);
                switch (currentAvailability)
                {
                    case ContactAvailability.Busy:              // Busy
						SetStateColor(STATE_COLOR_BUSY);
                        break;
                    case ContactAvailability.BusyIdle:          // Busy and idle
						SetStateColor(STATE_COLOR_BUSY_IDLE);
                        break;
                    case ContactAvailability.Free:              // Available
						SetStateColor(STATE_COLOR_AVAILABLE);
                        break;
                    case ContactAvailability.FreeIdle:          // Available and idle
						SetStateColor(STATE_COLOR_AVAILABLE_IDLE);
                        break;
                    case ContactAvailability.Away:              // Inactive/away, off work, appear away
                    case ContactAvailability.TemporarilyAway:   // Be right back
						SetStateColor(STATE_COLOR_AWAY);
                        break;
                    case ContactAvailability.DoNotDisturb:      // Do not disturb
						SetStateColor(STATE_COLOR_DO_NOT_DISTURB);
                        break;
                    case ContactAvailability.Offline:           // Offline
						SetStateColor(STATE_COLOR_OFF);
                        break;
                    default:
                        break;
                }

                Debug.WriteLine(currentAvailability.ToString());
            }
        }

        void SetBlink1State(Rgb color)
        {
            bool setColorResult = false;

            if (blink1.IsConnected)
            {
                setColorResult = blink1.SetColor(color);
                if (setColorResult)
                {
                    Debug.WriteLine("Successful set blink1 to {0},{1},{2}", color.Red, color.Green, color.Blue);
                }
                else
                {
                    Debug.WriteLine("Error setting blink1 to {0},{1},{2}", color.Red, color.Green, color.Blue);
                }
            }
        }

        void SetStateColor(StateColor stateColor)
        {
			arduino.SetLEDs(stateColor.arduinoColor);
            SetBlink1State(stateColor.blinkColor);
			trayIcon.Icon = stateColor.trayIcon;
        }

        void lyncClient_StateChanged(object sender, ClientStateChangedEventArgs e)
        {
            switch (e.NewState)
            {
                case ClientState.Initializing:
                    break;

                case ClientState.Invalid:
                    break;

                case ClientState.ShuttingDown:
					break;

                case ClientState.SignedIn:
                    lyncClient.Self.Contact.ContactInformationChanged += Contact_ContactInformationChanged;
                    SetCurrentContactState();
                    break;

                case ClientState.SignedOut:
                    trayIcon.ShowBalloonTip(1000, "", "You signed out in Lync. Switching to manual mode.", ToolTipIcon.Info);
					SetLyncIntegrationMode(false);
					break;

                case ClientState.SigningIn:
                    break;

                case ClientState.SigningOut:
                    break;

                case ClientState.Uninitialized:
                    break;

                default:
                    break;
            }
        }

        void Contact_ContactInformationChanged(object sender, ContactInformationChangedEventArgs e)
        {
            if (e.ChangedContactInformation.Contains(ContactInformationType.Availability))
            {
                SetCurrentContactState();
            }
        }

        void ConversationManager_ConversationAdded(object sender, ConversationManagerEventArgs e)
        {
            arduino.SetCallerId(e.Conversation.Participants[1].Contact.GetContactInformation(ContactInformationType.DisplayName).ToString());
        }

        void ConversationManager_ConversationRemoved(object sender, ConversationManagerEventArgs e)
        {
            arduino.ClearCallerId();

			if (lyncClient.State == ClientState.SignedIn) {
				int numOfConversations = lyncClient.ConversationManager.Conversations.Count;
				if (numOfConversations != 0)
				{
					Conversation lastConversation = lyncClient.ConversationManager.Conversations[numOfConversations - 1];					
					arduino.SetCallerId(lastConversation.Participants[1].Contact.GetContactInformation(ContactInformationType.DisplayName).ToString());
				}
			}
		}

		private void OnTimedStateCheckEvent(Object source, System.Timers.ElapsedEventArgs e)
		{
			// Try to reconnect to serial port if connection was lost
			if (!arduino.Port.IsOpen)
			{
				arduino.Dispose();
				if (Properties.Settings.Default.ArduinoSerialPort > 0)
				{
					arduino.OpenPort("COM" + Properties.Settings.Default.ArduinoSerialPort.ToString());
					// timing problem if we set the state to fast after connection, wait 1000ms
					Thread.Sleep(1000);
				}
			}

			// Try to reconnect to Lync/Skype
			//if (!isLyncIntegratedMode)
			//{
			//	GetLyncClient();
			//}


			SetCurrentContactState();
		}

		private void OnApplicationExit(object sender, EventArgs e)
        {
			// Set color OFF
			SetStateColor(STATE_COLOR_OFF);

            // Cleanup so that the icon will be removed when the application is closed
            trayIcon.Visible = false;

            // stop (USB) ManagementEventWatcher
            usbWatcher.Stop();
            usbWatcher.Dispose();

            // Close blink Connection and switch off LED
            if (blink1.IsConnected)
            {
                blink1.Close();
            }

            if (arduino.Port.IsOpen)
            {
                arduino.Dispose();
            }
                
        }
        
        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
        }

        private void CloseMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void aboutMenuItem_Click(object sender, EventArgs e)
        {
            AboutForm about = new AboutForm();
            about.ShowDialog();
        }

        private void settingsMenuItem_Click(object sender, EventArgs e)
        {
            SettingsForm settings = new SettingsForm();
            settings.ShowDialog();
        }

        private void OffMenuItem_Click(object sender, EventArgs e)
        {
			SetStateColor(STATE_COLOR_OFF);
        }

        private void AwayMenuItem_Click(object sender, EventArgs e)
        {
			SetStateColor(STATE_COLOR_AWAY);
        }

        private void BusyMenuItem_Click(object sender, EventArgs e)
        {
			SetStateColor(STATE_COLOR_BUSY);
        }

        private void AvailableMenuItem_Click(object sender, EventArgs e)
        {
			SetStateColor(STATE_COLOR_AVAILABLE);
        }

        private void DoNotDisturbMenuItem_Click(object sender, EventArgs e)
        {
			SetStateColor(STATE_COLOR_DO_NOT_DISTURB);
        }

        // Watch for USB changes to detect blink(1) removal
        private void InitializeUSBWatcher()
        {
            usbWatcher = new ManagementEventWatcher();
            var query = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent");
            usbWatcher.EventArrived += new EventArrivedEventHandler(watcher_EventArrived);
            usbWatcher.Query = query;
            usbWatcher.Start();
        }

        private void watcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            // Check if blink was removed
            // do not know what we do then, some hint to user?
            InitializeBlink1();

            if (blink1.IsConnected)
            {
                Debug.WriteLineIf(blink1.IsConnected, "USB change, Blink(1) available");

                // timing problem in blink(1) if we set the state to fast after plugin change, wait 100ms
                Thread.Sleep(100);
                SetCurrentContactState();
            }
            else
            {
                Debug.WriteLineIf(!blink1.IsConnected, "USB change, Blink(1) not available");
            }
        }
    }
}
