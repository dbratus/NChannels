﻿// Copyright (C) 2014 Dmitry Bratus
//
// The use of this source code is governed by the license
// that can be found in the LICENSE file.

using System;
using System.Threading.Tasks;

namespace NChannels
{
	public static class TimeSpanExtensions
	{
		/// <summary>
		/// Creates a channel which emits the current time after
		/// a specified delay.
		/// </summary>
		/// <param name="delay">The time after which the current time is emitted.</param>
		/// <returns>
		/// A channel which emits the current time after a specified delay.
		/// </returns>
		public static Chan<DateTime> After(this TimeSpan delay)
		{
			var chan = new Chan<DateTime>();
			WaitAndSend(delay, chan);
			return chan;
		}

		private static async void WaitAndSend(TimeSpan delay, Chan<DateTime> chan)
		{
			await Task.Delay(delay);
			await chan.Send(DateTime.Now);
			chan.Close();
		}
	}
}
