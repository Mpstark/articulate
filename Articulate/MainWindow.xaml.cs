﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Forms;
using System.Drawing;
using MahApps.Metro.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using SierraLib.GlobalHooks;
using System.Reactive.Concurrency;
using System.Threading;

namespace Articulate
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : MetroWindow, IDisposable
	{
		NotifyIcon ni;
		VoiceRecognizer recognizer;

		Settings settings;

		IObservable<EventPattern<RoutedPropertyChangedEventArgs<double>>> ConfidenceObserver;
		IDisposable ConfidenceObserverSubscription;

		AutoResetEvent PushToTalkRelease;

		public MainWindow()
		{
			InitializeComponent();

			PushToTalkRelease = new AutoResetEvent(false);

			ni = new System.Windows.Forms.NotifyIcon();

			ni.Icon = Properties.Resources.Main;
			ni.Visible = true;
			ni.Text = "Articulate";
			ni.DoubleClick += (sender, args) =>
				{
					this.Show();
					this.WindowState = WindowState.Normal;
				};
			
			settings = Articulate.Settings.Load();

			#region Rx Event Handlers

			var ConfidenceEvent = Observable.FromEventPattern<RoutedPropertyChangedEventArgs<double>>(ConfidenceMargin, "ValueChanged");

			ConfidenceObserver = ConfidenceEvent.Sample(TimeSpan.FromMilliseconds(500)).ObserveOn(ThreadPoolScheduler.Instance);
			ConfidenceEvent.Sample(TimeSpan.FromMilliseconds(50)).ObserveOnDispatcher().Subscribe(args =>
			{
				ConfidenceMarginNumber.Content = Math.Floor(args.EventArgs.NewValue).ToString();
			});

			ConfidenceObserverSubscription = ConfidenceObserver.Subscribe(args =>
			{
				settings.ConfidenceMargin = (int)args.EventArgs.NewValue;
				settings.Save();

					if (recognizer != null)
						recognizer.ConfidenceMargin = (int)args.EventArgs.NewValue;
				});

			#endregion
		}


		#region MVVM Properties

		public static DependencyProperty ArticulateStateProperty = DependencyProperty.Register("State", typeof(string), typeof(MainWindow), new PropertyMetadata("LOADING..."));
		public string State
		{
			get { return (string)GetValue(ArticulateStateProperty); }
			set { SetValue(ArticulateStateProperty, value); }
		}

		public static DependencyProperty ArticulateErrorMessageProperty = DependencyProperty.Register("ErrorMessage", typeof(string), typeof(MainWindow), new PropertyMetadata(""));
		public string ErrorMessage
		{
			get { return (string)GetValue(ArticulateErrorMessageProperty); }
			set { SetValue(ArticulateErrorMessageProperty, value); }
		}

		#endregion

		#region Window Events

		protected override void OnStateChanged(EventArgs e)
		{
			if (WindowState == System.Windows.WindowState.Minimized)
				this.Hide();

			base.OnStateChanged(e);
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			PTTKey.Content = settings.PTTKey.ToString();
			ListenMode.SelectedIndex = (int)settings.Mode;
			ConfidenceMargin.Value = settings.ConfidenceMargin;

			if (!settings.Applications.Any())
				settings.Applications.AddRange(new[] {
					"arma",
					"arma2",
					"arma2co",
					"takeonh",
					"arma3"
				});

			LoadRecognizer();
			
			HookManager.KeyDown += HookManager_KeyDown;
			HookManager.KeyUp += HookManager_KeyUp;
		}

		private void Window_Closing(object sender, CancelEventArgs e)
		{
			if (ni != null)
				ni.Visible = false;

			if(ConfidenceObserverSubscription != null)
				ConfidenceObserverSubscription.Dispose();
		}

		#endregion

		#region Recognition

		private void LoadRecognizer()
		{
			recognizer = new VoiceRecognizer();

			// something happened with the setup of the VoiceRecognizer (no mic, etc.)
			if (recognizer.State == VoiceRecognizer.VoiceRecognizerState.Error)
			{
				State = "FAILED";
				ErrorMessage = recognizer.SetupError;
				ErrorFlyout.IsOpen = true;
			}
			else
			{
				recognizer.ConfidenceMargin = settings.ConfidenceMargin;
				recognizer.MonitoredExecutables.AddRange(settings.Applications);
                recognizer.CommandAccepted += recognizer_CommandAccepted;
				recognizer.CommandRejected += recognizer_CommandRejected;


				Enabled = settings.Mode == Articulate.ListenMode.Continuous || settings.Mode == Articulate.ListenMode.PushToIgnore;
			}
		}

		void recognizer_CommandAccepted(object sender, CommandDetectedEventArgs e)
		{
			Trace.WriteLine("Accepted command: " + e.Phrase + " " + e.Confidence);

			LastCommand.Content = e.Phrase;

			if (settings.Mode == Articulate.ListenMode.PushToArm) Enabled = false;
		}

		void recognizer_CommandRejected(object sender, CommandDetectedEventArgs e)
		{
			Trace.WriteLine("Rejected command: " + e.Phrase + " " + e.Confidence);

			LastCommand.Content = "What was that?";

			// TODO: Decide whether or not Push To Arm should keep trying until it gets a match
			if (settings.Mode == Articulate.ListenMode.PushToArm) Enabled = false;
		}

		#endregion

		#region Window Command Buttons

		private void Settings_Click(object sender, RoutedEventArgs e)
		{
			SettingsFlyout.IsOpen = true;
		}

		private void About_Click(object sender, RoutedEventArgs e)
		{
			AboutFlyout.IsOpen = true;
		}

		#endregion

		#region Window Buttons

		private void ReloadRecognizer_Click(object sender, RoutedEventArgs e)
		{
			ErrorFlyout.IsOpen = false;
			LoadRecognizer();
		}

		private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
			e.Handled = true;
		}

		#endregion

		#region PTT
				
		public bool Enabled
		{
			get { return recognizer.State == VoiceRecognizer.VoiceRecognizerState.Listening || recognizer.State == VoiceRecognizer.VoiceRecognizerState.ListeningOnce; }
			set
			{
				if (recognizer == null) return;

				if (value)
				{
					if (settings.Mode == Articulate.ListenMode.PushToArm) recognizer.ListenOnce();
					else recognizer.StartListening();

					State = "LISTENING";
				}
				else
				{
					recognizer.StopListening();
					State = "OFFLINE";
				}
			}
		}


		
		void HookManager_KeyUp(object sender, System.Windows.Forms.KeyEventArgs e)
		{
			if(settings.PTTKey == System.Windows.Forms.Keys.None || e.KeyCode != settings.PTTKey) return;

			if (settings.Mode == Articulate.ListenMode.PushToArm) return; // Don't disable if we're armed
			if (settings.Mode == Articulate.ListenMode.Continuous) return;

			PushToTalkRelease.Reset();

			ThreadPool.RegisterWaitForSingleObject(PushToTalkRelease, (state, completed) =>
			{
				if (completed)
					Dispatcher.Invoke(() =>
					{
						Enabled = settings.Mode == Articulate.ListenMode.Continuous || settings.Mode == Articulate.ListenMode.PushToIgnore;
					});
			}, null, 500, true);
		}

		void HookManager_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
		{
			if (ListeningForNewPTT)
			{
				ListeningForNewPTT = false;

				if (e.KeyCode == System.Windows.Forms.Keys.Escape)
				{
					settings.Mode = Articulate.ListenMode.Continuous;
					settings.PTTKey = System.Windows.Forms.Keys.None;
				}
				else
					settings.PTTKey = e.KeyCode;

				PTTKey.Content = settings.PTTKey.ToString();
				ListenMode.SelectedIndex = (int)settings.Mode;

				Enabled = settings.Mode == Articulate.ListenMode.Continuous || settings.Mode == Articulate.ListenMode.PushToIgnore;
				settings.Save();

				return;
			}

			if (settings.PTTKey == System.Windows.Forms.Keys.None || e.KeyCode != settings.PTTKey) return;
			if (settings.Mode == Articulate.ListenMode.Continuous) return;

			PushToTalkRelease.Set();

			Enabled = settings.Mode == Articulate.ListenMode.PushToTalk || settings.Mode == Articulate.ListenMode.PushToArm;
		}

		#endregion

		#region Settings

		private bool ListeningForNewPTT = false;

		private void PTTKey_Click(object sender, RoutedEventArgs e)
		{
			ListeningForNewPTT = true;
		}

		private void ListenMode_Selected(object sender, RoutedEventArgs e)
		{
			settings.Mode = (Articulate.ListenMode)(ListenMode.SelectedIndex);
			
			Enabled = settings.Mode == Articulate.ListenMode.Continuous || settings.Mode == Articulate.ListenMode.PushToIgnore;

			settings.Save();
		}
		
		#endregion

        #region IDispose Implementation
        public void Dispose()
		{
			if (recognizer != null)
			{
				recognizer.Dispose();
				recognizer = null;
			}

			if (ni != null)
			{
				ni.Dispose();
				ni = null;
			}
        }
        #endregion
    }
}
