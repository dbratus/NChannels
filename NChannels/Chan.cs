// Copyright (C) 2014 Dmitry Bratus
//
// The use of this source code is governed by the license
// that can be found in the LICENSE file.

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace NChannels
{
	/// <summary>
	/// <para>
	/// Asynchronous channel with limited buffer size.
	/// </para>
	/// 
	/// <para>
	/// Channels link two or more asynchronous task and allow them
	/// to pass items without blocking and keeping the limited number of
	/// items in backlog. If a send/receive operation cannot be completed
	/// immediately, a channel does not block. Instead, it returns a task that
	/// represents the completion of the operation. Thus, the tasks can wait
	/// for send/receive by using 'await' operator.
	/// </para>
	/// </summary>
	/// <typeparam name="T">Item type.</typeparam>
	public sealed class Chan<T>
	{
		private struct Sender
		{
			public T Item;
			public TaskCompletionSource<bool> Completion;
		}

		private readonly object _syncRoot = new object();
		private readonly int _bufferSize;
		private readonly T[] _queue;
		private long _writePointer;
		private long _readPointer;
		private readonly Queue<Sender> _blockedSenders;
		private readonly Queue<TaskCompletionSource<ChanResult<T>>> _blockedReceivers;
		private bool _isClosed;
		private Action _onceReceiveReady;
		private Action _onceSendReady;

		/// <summary>
		/// Creates a channel.
		/// </summary>
		/// <param name="bufferSize">
		/// The capacity of the buffer. Indicates how many items
		/// a channel can accept without blocking if no task is
		/// receiving them.
		/// </param>
		public Chan(int bufferSize = 1)
		{
			if (bufferSize < 1)
			{
				throw new ArgumentException("maxBufferSize");
			}

			_bufferSize = bufferSize;
			_queue = new T[bufferSize];
			_blockedSenders = new Queue<Sender>();
			_blockedReceivers = new Queue<TaskCompletionSource<ChanResult<T>>>();
		}

		/// <summary>
		/// <para>
		/// Asynchronously receives an item.
		/// </para>
		/// 
		/// <para>
		/// The task returned by Receive completes when either an item is received
		/// or the channel is closed. If the channel is closed before the call,
		/// the task completes immediately.
		/// </para>
		/// </summary>
		/// <returns>The task representing the completion of the operation.</returns>
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
							Result = _queue[(_readPointer++) % _bufferSize],
							IsSuccess = true
						}
					);

					if (_blockedSenders.Count > 0)
					{
						var sender = _blockedSenders.Dequeue();

						_queue[(_writePointer++) % _bufferSize] = sender.Item;

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

		/// <summary>
		/// <para>
		/// Asynchronously sends an item to the channel.
		/// </para>
		/// 
		/// <para>
		/// The task returned by Send completes when the item is either put to
		/// the buffer or received.
		/// </para>
		/// </summary>
		/// <param name="item">The item to be sent.</param>
		/// <returns>The task representing the completion of the operation.</returns>
		/// <exception cref="ChannelClosedException">
		/// When the channel is closed.
		/// </exception>
		public Task Send(T item)
		{
			if (_isClosed)
			{
				throw new ChannelClosedException();
			}

			var completion = new TaskCompletionSource<bool>();

			lock (_syncRoot)
			{
				if (_writePointer - _readPointer < _bufferSize)
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
						_queue[(_writePointer++) % _bufferSize] = item;
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

		/// <summary>
		/// Closes the channel.
		/// </summary>
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

		private void OnReceiveReady()
		{
			Action action;

			if ((action = Interlocked.Exchange(ref _onceReceiveReady, null)) != null)
			{
				action();
			}
		}

		private void OnSendReady()
		{
			Action action;

			if ((action = Interlocked.Exchange(ref _onceSendReady, null)) != null)
			{
				action();
			}
		}

		internal void OnceReceiveReady(Action action)
		{
			if (action != null)
			{
				lock (_syncRoot)
				{
					if (_isClosed || _readPointer != _writePointer)
					{
						action();
					}
					else
					{
						Interlocked.Exchange(ref _onceReceiveReady, action);
					}
				}
			}
			else
			{
				Interlocked.Exchange(ref _onceReceiveReady, null);
			}
		}

		internal void OnceSendReady(Action action)
		{
			if (action != null)
			{
				lock (_syncRoot)
				{
					if (_writePointer - _readPointer < _bufferSize)
					{
						action();
					}
					else
					{
						Interlocked.Exchange(ref _onceSendReady, action);
					}
				}
			}
			else
			{
				Interlocked.Exchange(ref _onceSendReady, null);
			}
		}
	}

	/// <summary>
	/// Result of the Receive operation.
	/// </summary>
	/// <typeparam name="T">Item type.</typeparam>
	public struct ChanResult<T>
	{
		/// <summary>
		/// The item received. If the item has not been received,
		/// the Result if default(T).
		/// </summary>
		public T Result { get; internal set; }

		/// <summary>
		/// It is false if the channel has been closed; otherwsie, true,
		/// </summary>
		public bool IsSuccess { get; internal set; }
	}

	/// <summary>
	/// The exception thrown when an attempt has been made to send to
	/// a closed channel.
	/// </summary>
	public class ChannelClosedException : InvalidOperationException
	{
		internal ChannelClosedException() : base("Channel is closed")
		{
		}
	}
}

