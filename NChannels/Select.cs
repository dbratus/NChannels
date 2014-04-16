// Copyright (C) 2014 Dmitry Bratus
//
// The use of this source code is governed by the license
// that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NChannels
{
	public class Select
	{
		private readonly object _syncRoot = new object();
		private readonly Random _rng = new Random();
		private readonly List<Func<Task>> _immediateSelections = new List<Func<Task>>();
		private readonly List<Action> _listenerClearings = new List<Action>(); 

		private TaskCompletionSource<Func<Task>> _selection; 
		private bool _hasSelected;
		private bool _casesBuilt;

		public Select Begin()
		{
			_hasSelected = false;
			_casesBuilt = false;
			_selection = new TaskCompletionSource<Func<Task>>();

			return this;
		}

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

					if (_casesBuilt)
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

					if (_casesBuilt)
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

		public async Task End()
		{
			_casesBuilt = true;

			if (_immediateSelections.Count > 0)
			{
				await _immediateSelections[_rng.Next(0, _immediateSelections.Count)]();
				_immediateSelections.Clear();
				return;
			}

			var selectedCase = await _selection.Task;
			await selectedCase();

			foreach (var clear in _listenerClearings)
			{
				clear();
			}
			
			_listenerClearings.Clear();
		}
	}
}

