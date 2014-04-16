// Copyright (C) 2014 Dmitry Bratus
//
// The use of this source code is governed by the license
// that can be found in the LICENSE file.

using NChannels;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace NChannelsTest
{
	[TestFixture]
	public class ChanTest
	{
		[Test]
		public void SendReceive()
		{
			var chan = new Chan<int>();

			chan
				.Send(Enumerable.Range(0, 10))
				.ContinueWith(t => chan.Close());

			var cnt = 0;
			var collection =
				chan.ForEach(item => cnt++);

			if (!collection.Wait(TimeSpan.FromSeconds(10)))
			{
				Assert.Fail();
			}

			Assert.AreEqual(10, cnt);
		}

		[Test]
		public void Merge()
		{
			var chan1 = new Chan<int>();
			var chan2 = new Chan<int>();

			chan1
				.Send(Enumerable.Range(0, 10))
				.ContinueWith(t => chan1.Close());
			chan2
				.Send(Enumerable.Range(0, 10))
				.ContinueWith(t => chan2.Close());
			
			var mergedChan = chan1.Merge(chan2);

			var cnt = 0;
			var collection =
				mergedChan.ForEach(item => cnt++);

			if (!collection.Wait(TimeSpan.FromSeconds(10)))
			{
				Assert.Fail();
			}

			Assert.AreEqual(20, cnt);
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

