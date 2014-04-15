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
				ChanResult<int> result;

				while((result = await chan.Receive()).IsSuccess)
				{
					Console.WriteLine(result.Result);

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
				var select = new Select();

				while (!(leftClosed && rightClosed))
				{
					await select
						.Begin()
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
	}
}

