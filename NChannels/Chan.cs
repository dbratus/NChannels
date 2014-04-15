﻿// Copyright (C) 2014 Dmitry Bratus
//
// The use of this source code is governed by the license
// that can be found in the LICENSE file.

using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace NChannels
{
	public class Chan<T> : IObservableChannel
	{
		private struct Sender
		{
			public T Item;
			public TaskCompletionSource<bool> Completion;
		}

		private readonly object _syncRoot = new object();
		private readonly int _maxBufferSize;
		private readonly T[] _queue;
		private long _writePointer;
		private long _readPointer;
		private readonly Queue<Sender> _blockedSenders;
		private readonly Queue<TaskCompletionSource<ChanResult<T>>> _blockedReceivers;
		private bool _isClosed;

		public Chan(int maxBufferSize = 1)
		{
			if (maxBufferSize < 1)
			{
				throw new ArgumentException("maxBufferSize");
			}

			_maxBufferSize = maxBufferSize;
			_queue = new T[maxBufferSize];
			_blockedSenders = new Queue<Sender>();
			_blockedReceivers = new Queue<TaskCompletionSource<ChanResult<T>>>();
		}

		public Task<ChanResult<T>> Receive()
		{
			var completion = new TaskCompletionSource<ChanResult<T>>();

			lock (_syncRoot)
			{
				if (_readPointer != _writePointer)
				{
					completion.SetResult
					(
						new ChanResult<T> 
						{	
							Result = _queue[(_readPointer++) % _maxBufferSize],
							IsSuccess = true
						}
					);

					if (_blockedSenders.Count > 0)
					{
						var sender = _blockedSenders.Dequeue();

						_queue[(_writePointer++) % _maxBufferSize] = sender.Item;

						if (!_isClosed)
						{
							sender.Completion.SetResult(true);
						}
						else
						{
							sender.Completion.SetException(new ChannelClosedException());
						}
					}
					else
					{
						OnSendReady();
					}
				}
				else
				{
					if (!_isClosed)
					{
						_blockedReceivers.Enqueue(completion);
						OnSendReady();
					}
					else
					{
						completion.SetResult
						(
							new ChanResult<T> 
							{	
								Result = default(T),
								IsSuccess = false
							}
						);
					}
				}
			}

			return completion.Task;
		}

		public Task Send(T item)
		{
			if (_isClosed)
			{
				throw new ChannelClosedException();
			}

			var completion = new TaskCompletionSource<bool>();

			lock (_syncRoot)
			{
				if (_writePointer - _readPointer < _maxBufferSize)
				{
					if (_blockedReceivers.Count > 0)
					{
						_blockedReceivers.Dequeue().SetResult
						(
							new ChanResult<T> 
							{
								Result = item, 
								IsSuccess = true
							}
						);
					}
					else
					{
						_queue[(_writePointer++) % _maxBufferSize] = item;
						OnReceiveReady();
					}

					completion.SetResult(true);
				}
				else
				{
					_blockedSenders.Enqueue(new Sender { Item = item, Completion = completion });
				}
			}

			return completion.Task;
		}

		public void Close()
		{
			lock (_syncRoot)
			{
				_isClosed = true;

				while (_blockedReceivers.Count > 0)
				{
					_blockedReceivers.Dequeue().SetResult
					(
						new ChanResult<T>
						{
							Result = default(T),
							IsSuccess = false
						}
					);
				}

				while (_blockedSenders.Count > 0)
				{
					_blockedSenders.Dequeue().Completion.SetException
					(
						new ChannelClosedException()
					);
				}

				OnReceiveReady();
			}
		}

		private event EventHandler _receiveReady;
		public event EventHandler ReceiveReady
		{
			add
			{
				lock (_syncRoot)
				{
					if (_isClosed || _readPointer != _writePointer)
					{
						value.Invoke(this, EventArgs.Empty);
					}

					_receiveReady += value;
				}
			}
			remove
			{
				_receiveReady -= value;
			}
		}

		protected virtual void OnReceiveReady()
		{
			EventHandler handler = _receiveReady;

			if (handler != null)
			{
				handler(this, EventArgs.Empty);
			}
		}

		private event EventHandler _sendReady;
		public event EventHandler SendReady
		{
			add
			{
				lock (_syncRoot)
				{
					if (_writePointer - _readPointer < _maxBufferSize)
					{
						value.Invoke(this, EventArgs.Empty);
					}

					_sendReady += value;
				}
			}
			remove
			{
				_sendReady -= value;
			}
		}

		protected virtual void OnSendReady()
		{
			EventHandler handler = _sendReady;

			if (handler != null)
			{
				handler(this, EventArgs.Empty);
			}
		}
	}

	public interface IObservableChannel
	{
		event EventHandler ReceiveReady;
		event EventHandler SendReady;
	}

	public struct ChanResult<T>
	{
		public T Result { get; internal set; }
		public bool IsSuccess { get; internal set; }
	}

	public class ChannelClosedException : InvalidOperationException
	{
		public ChannelClosedException() : base("Channel is closed")
		{
		}
	}
}
