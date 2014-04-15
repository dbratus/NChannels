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
		private struct ChanHandler
		{
			public IObservableChannel Channel;
			public EventHandler Handler;
		}

		private readonly object _syncRoot = new object();
		private readonly Random _rng = new Random();
		private readonly List<ChanHandler> _handlers = new List<ChanHandler>();
		private readonly List<Func<Task>> _immediateSelections = new List<Func<Task>>();

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
			EventHandler onReceiveReady =
				(sender, args) => 
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
				};
			channel.ReceiveReady += onReceiveReady;

			_handlers.Add(new ChanHandler { Channel = channel, Handler = onReceiveReady });

			return this;
		}

		public Select CaseAsync<T>(Chan<T> channel, Func<T, bool, Task> action)
		{
			EventHandler onReceiveReady =
				(sender, args) => 
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
				};
			channel.ReceiveReady += onReceiveReady;

			_handlers.Add(new ChanHandler { Channel = channel, Handler = onReceiveReady });

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

			foreach (var chanHandler in _handlers)
			{
				chanHandler.Channel.ReceiveReady -= chanHandler.Handler;
			}
		}
	}
}

