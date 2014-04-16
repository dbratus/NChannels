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
		public static async Task Send<T>(this Chan<T> chan, IEnumerable<T> items)
		{
			foreach (var item in items)
			{
				await chan.Send(item);
			}
		}

		public static Chan<T> Merge<T>(this Chan<T> left, Chan<T> right, int maxBufferSize = 1)
		{
			var result = new Chan<T>(maxBufferSize);
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

		public static Chan<T> Where<T>(this Chan<T> source, Predicate<T> condition, int maxBufferSize = 1)
		{
			var target = new Chan<T>(maxBufferSize);
			DoWhere(source, target, condition);
			return target;
		}

		public static async void DoWhere<T>(Chan<T> source, Chan<T> target, Predicate<T> condition)
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

		public static Chan<TOut> Select<TIn, TOut>(this Chan<TIn> source, Func<TIn, TOut> map, int maxBufferSize = 1)
		{
			var target = new Chan<TOut>(maxBufferSize);
			DoSelect(source, target, map);
			return target;
		}

		public static async void DoSelect<TIn, TOut>(Chan<TIn> source, Chan<TOut> target, Func<TIn, TOut> map)
		{
			ChanResult<TIn> result;

			while ((result = await source.Receive()).IsSuccess)
			{
				await target.Send(map(result.Result));
			}

			target.Close();
		}

		public static async Task Forward<T>(this Chan<T> source, Chan<T> target)
		{
			ChanResult<T> result;

			while ((result = await source.Receive()).IsSuccess)
			{
				await target.Send(result.Result);
			}

			target.Close();
		}

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

		public static async Task Purge<T>(this Chan<T> chan)
		{
			while ((await chan.Receive()).IsSuccess)
			{
			}
		}

		public static async Task<long> Count<T>(this Chan<T> chan)
		{
			var cnt = 0L;

			while ((await chan.Receive()).IsSuccess)
			{
				cnt++;
			}

			return cnt;
		}

		public static async Task ForEach<T>(this Chan<T> chan, Action<T> action)
		{
			ChanResult<T> result;

			while ((result = await chan.Receive()).IsSuccess)
			{
				action(result.Result);
			}
		}

		public static async Task ForEach<T>(this Chan<T> chan, Func<T, Task> action)
		{
			ChanResult<T> result;

			while ((result = await chan.Receive()).IsSuccess)
			{
				await action(result.Result);
			}
		}
	}

	public static class ChanEnumerableExtensions
	{
		public static Chan<T> Merge<T>(this IEnumerable<Chan<T>> channels, int maxBufferSize = 1)
		{
			Chan<T> left = null;

			foreach (var chan in channels)
			{
				left = left == null ? chan : left.Merge(chan, maxBufferSize);
			}

			return left;
		}
	}
}
