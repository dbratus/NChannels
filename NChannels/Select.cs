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
	public class Select
	{
		private static long _rng;

		private readonly object _syncRoot = new object();
		private readonly List<Func<Task>> _immediateSelections = new List<Func<Task>>();
		private readonly List<Action> _listenerClearings = new List<Action>();
		private readonly TaskCompletionSource<Func<Task>> _selection = new TaskCompletionSource<Func<Task>>(); 

		private bool _hasSelected;
		private int _casesBuilt;

		public Select Case<T>(Chan<T> channel, Action<T, bool> action)
		{
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
									_selection.SetResult(selection);

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

		public Select CaseAsync<T>(Chan<T> channel, Func<T, bool, Task> action)
		{
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
									_selection.SetResult(selection);

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

		public async Task End()
		{
			Interlocked.Increment(ref _casesBuilt);

			if (_immediateSelections.Count > 0)
			{
				await _immediateSelections[(int)(Interlocked.Increment(ref _rng) % _immediateSelections.Count)]();
			}
			else
			{
				await (await _selection.Task)();
			}
		}
	}
}

