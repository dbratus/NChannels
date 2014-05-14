// Copyright (C) 2014 Dmitry Bratus
//
// The use of this source code is governed by the license
// that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NChannels
{
	/// <summary>
	/// <para>
	/// Channel multiplexer.
	/// </para>
	/// 
	/// <para>
	/// By using channel multiplexer, a task can wait for items
	/// on multiple channels simultaneously.
	/// </para>
	/// 
	/// <para>
	/// An instance of a multiplexer can be used only once.
	/// </para>
	/// </summary>
	public class Select
	{
		private static long _rng;

		private readonly object _syncRoot = new object();
		private readonly List<Func<Task>> _immediateSelections = new List<Func<Task>>();
		private readonly List<Action> _listenerClearings = new List<Action>();
		private readonly TaskCompletionSource<Func<Task>> _selection = new TaskCompletionSource<Func<Task>>(); 

		private bool _hasSelected;
		private int _casesBuilt;
		private bool _hasBeenUsed;

		/// <summary>
		/// Adds a synchronous handler for a channel.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="channel">The channel to receive items from.</param>
		/// <param name="action">
		/// The action to invoke as an item is received or the channel is closed.
		/// The first argument of the action is the item, the second is the flag
		/// indicating whether an item has been received.
		/// </param>
		/// <returns>This instance.</returns>
		public Select Case<T>(Chan<T> channel, Action<T, bool> action)
		{
			if (_hasBeenUsed) throw new InvalidOperationException("The instance has already been used.");

			channel.OnceReceiveReady
			(
				() =>
				{
					Func<Task> selection = async () =>
					{
						var result = await channel.Receive();

						action(result.Result, result.IsSuccess);
					};

					if (_casesBuilt > 0)
					{
						if (!_hasSelected)
						{
							lock (_syncRoot)
							{
								if (!_hasSelected)
								{
									SetResult(selection);

									_hasSelected = true;
								}
							}
						}
					}
					else
					{
						_immediateSelections.Add(selection);
					}
				}
			);
			
			_listenerClearings.Add(() => channel.OnceReceiveReady(null));

			return this;
		}

		/// <summary>
		/// Adds an asynchronous handler for a channel.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="channel">The channel to receive items from.</param>
		/// <param name="action">
		/// The action to invoke as an item is received or the channel is closed.
		/// The first argument of the action is the item, the second is the flag
		/// indicating whether an item has been received.
		/// </param>
		/// <returns>This instance.</returns>
		public Select Case<T>(Chan<T> channel, Func<T, bool, Task> action)
		{
			if (_hasBeenUsed) throw new InvalidOperationException("The instance has already been used.");

			channel.OnceReceiveReady
			(
				() =>
				{
					Func<Task> selection = async () =>
					{
						var result = await channel.Receive();

						await action(result.Result, result.IsSuccess);
					};

					if (_casesBuilt > 0)
					{
						if (!_hasSelected)
						{
							lock (_syncRoot)
							{
								if (!_hasSelected)
								{
									SetResult(selection);

									_hasSelected = true;
								}
							}
						}
					}
					else
					{
						_immediateSelections.Add(selection);
					}
				}
			);

			return this;
		}

		/// <summary>
		/// Closes the list of cases. After this call no methods of the
		/// multiplexer can be invoked.
		/// </summary>
		/// <returns>
		/// A task that completes as any of the added handlers complete.
		/// </returns>
		public async Task End()
		{
			if (_hasBeenUsed) throw new InvalidOperationException("The instance has already been used.");

			Interlocked.Increment(ref _casesBuilt);

			if (_immediateSelections.Count > 0)
			{
#pragma warning disable 4014
				Task.Delay(1).ContinueWith(_ => MakeRandomSelection());
#pragma warning restore 4014
			}

			await (await _selection.Task)();

			_hasBeenUsed = true;
		}

		private void SetResult(Func<Task> result)
		{
			lock (_selection)
			{
				if (_selection.Task.Status != TaskStatus.RanToCompletion)
				{
					_selection.SetResult(result);
				}
			}
		}

		private void MakeRandomSelection()
		{
			SetResult(_immediateSelections[(int)(Interlocked.Increment(ref _rng) % _immediateSelections.Count)]);
		}
	}
}

