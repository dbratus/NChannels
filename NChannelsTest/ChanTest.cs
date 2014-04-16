// Copyright (C) 2014 Dmitry Bratus
//
// The use of this source code is governed by the license
// that can be found in the LICENSE file.

using NChannels;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace NChannelsTest
{
	[TestFixture]
	public class ChanTest
	{
		[Test]
		public void SendReceive()
		{
			var chan = new Chan<int>();

			Func<Task> emit = async () =>
			{
				for (int i = 0; i < 10; i++) 
				{
					await chan.Send(i);
				}

				chan.Close();
			};

			int counter = 0;

			Func<Task> collect = async () => 
			{
				while((await chan.Receive()).IsSuccess)
				{
					counter++;
				}
			};

			emit();

			if (!collect().Wait(TimeSpan.FromSeconds(10)))
			{
				Assert.Fail();
			}

			Assert.AreEqual(10, counter);
		}

		[Test]
		public void Merge()
		{
			var chan1 = new Chan<int>();
			var chan2 = new Chan<int>();

			Func<Chan<int>, Task> emit = async chan =>
			{
				for (int i = 0; i < 10; i++)
				{
					await chan.Send(i);
				}

				chan.Close();
			};

			var mergedChan = new Chan<int>();

			Func<Chan<int>, Chan<int>, Task> merge = async (left, right) => 
			{
				var leftClosed = false;
				var rightClosed = false;

				while (!(leftClosed && rightClosed))
				{
					await new Select()
						.CaseAsync(left, async (item, ok) => 
						{
							if (ok)
							{
								await mergedChan.Send(item);
							}
							else
							{
								leftClosed = true;
							}
						})
						.CaseAsync(right, async (item, ok) => 
						{
							if (ok)
							{
								await mergedChan.Send(item);
							}
							else
							{
								rightClosed = true;
							}
						})
						.End();
				}

				mergedChan.Close();
			};

			int counter = 0;

			Func<Task> collect = async () =>
			{
				while ((await mergedChan.Receive()).IsSuccess)
				{
					counter++;
				}
			};

			emit(chan1);
			emit(chan2);
			merge(chan1, chan2);

			if (!collect().Wait(TimeSpan.FromSeconds(5)))
			{
				Assert.Fail();
			}

			Assert.AreEqual(20, counter);
		}

		[Test]
		public void Timeout()
		{
			Func<Task<bool>> doTests = async () => 
			{
				var result = true;
				var rng = new Random();

				for (int i = 0; i < 10; i++)
				{
					TimeSpan ts1, ts2;

					do
					{
						ts1 = TimeSpan.FromMilliseconds(rng.Next(10, 500));
						ts2 = TimeSpan.FromMilliseconds(rng.Next(10, 500));
					} while (Math.Abs((ts1 - ts2).TotalMilliseconds) < 100);

					var selection = default(TimeSpan);

					await new Select()
						.Case(ts1.After(), (t, ok) => selection = ts1)
						.Case(ts2.After(), (t, ok) => selection = ts2)
						.End();

					if ((ts1 < ts2 && selection != ts1) || (ts2 < ts1 && selection != ts2))
					{
						result = false;
						break;
					}
				}

				return result;
			};

			Assert.IsTrue(doTests().Result);
		}
	}
}

