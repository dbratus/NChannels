// Copyright (C) 2014 Dmitry Bratus
//
// The use of this source code is governed by the license
// that can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NChannels
{
	public static class ChanExtensions
	{
		/// <summary>
		/// Sends multiple items to a channel.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="chan">The channel.</param>
		/// <param name="items">The items to be sent.</param>
		/// <returns>A task representing the completion of the operation.</returns>
		public static async Task Send<T>(this Chan<T> chan, IEnumerable<T> items)
		{
			foreach (var item in items)
			{
				await chan.Send(item);
			}
		}

		/// <summary>
		/// Merges two channels into one. The order of the items in the
		/// resulting channel is not guaranteed.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="left">Left channel.</param>
		/// <param name="right">Right channel.</param>
		/// <param name="bufferSize">The the buffer size of the resulting channel.</param>
		/// <returns>The channel including items from both channels.</returns>
		public static Chan<T> Merge<T>(this Chan<T> left, Chan<T> right, int bufferSize = 1)
		{
			var result = new Chan<T>(bufferSize);
			DoMerge(left, right, result);
			return result;
		}

		private static async void DoMerge<T>(Chan<T> left, Chan<T> right, Chan<T> result)
		{
			var leftClosed = false;
			var rightClosed = false;

			while (!(leftClosed && rightClosed))
			{
				await new Select()
					.Case(left, async (item, ok) =>
					{
						if (ok)
						{
							await result.Send(item);
						}
						else
						{
							leftClosed = true;
						}
					})
					.Case(right, async (item, ok) =>
					{
						if (ok)
						{
							await result.Send(item);
						}
						else
						{
							rightClosed = true;
						}
					})
					.End();
			}

			result.Close();
		}

		/// <summary>
		/// Filters items received from a channel.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="source">The source channel.</param>
		/// <param name="condition">The condition that items must satisfy.</param>
		/// <param name="bufferSize">The the buffer size of the resulting channel.</param>
		/// <returns>
		/// A channel including only those items from the source channel which satisfy the condition.
		/// </returns>
		public static Chan<T> Where<T>(this Chan<T> source, Predicate<T> condition, int bufferSize = 1)
		{
			var target = new Chan<T>(bufferSize);
			DoWhere(source, target, condition);
			return target;
		}

		private static async void DoWhere<T>(Chan<T> source, Chan<T> target, Predicate<T> condition)
		{
			ChanResult<T> result;

			while ((result = await source.Receive()).IsSuccess)
			{
				if (condition(result.Result))
				{
					await target.Send(result.Result);
				}
			}

			target.Close();
		}

		/// <summary>
		/// Projects items from a channel.
		/// </summary>
		/// <typeparam name="TIn">Input items type.</typeparam>
		/// <typeparam name="TOut">Output items type.</typeparam>
		/// <param name="source">The source channel.</param>
		/// <param name="map">The transformation of the items.</param>
		/// <param name="bufferSize">The the buffer size of the resulting channel.</param>
		/// <returns>A channel of items from the source channel transformed by the map function.</returns>
		public static Chan<TOut> Select<TIn, TOut>(this Chan<TIn> source, Func<TIn, TOut> map, int bufferSize = 1)
		{
			var target = new Chan<TOut>(bufferSize);
			DoSelect(source, target, map);
			return target;
		}

		private static async void DoSelect<TIn, TOut>(Chan<TIn> source, Chan<TOut> target, Func<TIn, TOut> map)
		{
			ChanResult<TIn> result;

			while ((result = await source.Receive()).IsSuccess)
			{
				await target.Send(map(result.Result));
			}

			target.Close();
		}

		/// <summary>
		/// Forwards all items from the source to the target channel.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="source">The source channel.</param>
		/// <param name="target">The target channel.</param>
		/// <returns>A task representing the completion of the operation.</returns>
		public static async Task Forward<T>(this Chan<T> source, Chan<T> target)
		{
			ChanResult<T> result;

			while ((result = await source.Receive()).IsSuccess)
			{
				await target.Send(result.Result);
			}

			target.Close();
		}

		/// <summary>
		/// Forwards all items from the source to the target channels.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="source">The source channel.</param>
		/// <param name="targets">The target channels.</param>
		/// <returns>A task representing the completion of the operation.</returns>
		public static async Task Spread<T>(this Chan<T> source, IEnumerable<Chan<T>> targets)
		{
			var targetsArray = targets.ToArray();
			var completions = new Task[targetsArray.Length];

			ChanResult<T> result;

			while ((result = await source.Receive()).IsSuccess)
			{
				for (int i = 0; i < targetsArray.Length; i++)
				{
					completions[i] = targetsArray[i].Send(result.Result);
				}

				for (int i = 0; i < completions.Length; i++)
				{
					await completions[i];
				}
			}

			for (int i = 0; i < targetsArray.Length; i++)
			{
				targetsArray[i].Close();
			}
		}

		/// <summary>
		/// Receives all items from a channel until it's closed.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="source">The source channel.</param>
		/// <returns>A task representing the completion of the operation.</returns>
		public static async Task Purge<T>(this Chan<T> source)
		{
			while ((await source.Receive()).IsSuccess) { }
		}

		/// <summary>
		/// Counts the items received by a channel.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="source">The source channel.</param>
		/// <returns>A task representing the completion of the operation.</returns>
		public static async Task<long> Count<T>(this Chan<T> source)
		{
			var cnt = 0L;

			while ((await source.Receive()).IsSuccess)
			{
				cnt++;
			}

			return cnt;
		}

		/// <summary>
		/// Iterates over the items received from a channel calling
		/// a synchronous action for each.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="source">The source channel.</param>
		/// <param name="action">The action to call for each item.</param>
		/// <returns>A task representing the completion of the operation.</returns>
		public static async Task ForEach<T>(this Chan<T> source, Action<T> action)
		{
			ChanResult<T> result;

			while ((result = await source.Receive()).IsSuccess)
			{
				action(result.Result);
			}
		}

		/// <summary>
		/// Iterates over the items received from a channel calling
		/// an asynchronous action for each.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="source">The source channel.</param>
		/// <param name="action">The action to call for each item.</param>
		/// <returns>A task representing the completion of the operation.</returns>
		public static async Task ForEach<T>(this Chan<T> source, Func<T, Task> action)
		{
			ChanResult<T> result;

			while ((result = await source.Receive()).IsSuccess)
			{
				await action(result.Result);
			}
		}
	}

	public static class ChanEnumerableExtensions
	{
		/// <summary>
		/// Merges channels into one. The order of the items in the
		/// resulting channel is not guaranteed.
		/// </summary>
		/// <typeparam name="T">Item type.</typeparam>
		/// <param name="channels">The channels to merge.</param>
		/// <param name="bufferSize">The the buffer size of the resulting channel.</param>
		/// <returns>The channel including items from the all channels.</returns>
		public static Chan<T> Merge<T>(this IEnumerable<Chan<T>> channels, int bufferSize = 1)
		{
			Chan<T> left = null;

			foreach (var chan in channels)
			{
				left = left == null ? chan : left.Merge(chan, bufferSize);
			}

			return left;
		}
	}
}
