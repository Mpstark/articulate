﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Articulate
{
	public class Core : IDisposable
	{
		public Core()
		{
			Configuration = Settings.Load();
			Keybinder = new KeyMonitor(Configuration);
			Recognizer = new VoiceRecognizer();
			SoundPlayer = new SoundEffectsPlayer(Configuration);

			Recognizer.CommandAccepted += SoundPlayer.CommandAccepted;
			Recognizer.CommandRejected += SoundPlayer.CommandRejected;
			Recognizer.StartedListening += SoundPlayer.StartedListening;
			Recognizer.StoppedListening += SoundPlayer.StoppedListening;
		}

		#region Public Properties

		public Settings Configuration
		{ get; private set; }

		public KeyMonitor Keybinder
		{ get; private set; }

		public VoiceRecognizer Recognizer
		{ get; private set; }

		public SoundEffectsPlayer SoundPlayer
		{ get; private set; }

		#endregion

		public void Dispose()
		{
			if (Keybinder != null)
			{
				Keybinder.Dispose();
				Keybinder = null;
			}

			if (Recognizer != null)
			{
				Recognizer.Dispose();
				Recognizer.Dispose();
			}
		}
	}
}
