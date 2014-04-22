# NChannels

NChannels introduces the channel primitive that is very useful for asynchronous programming with async/await. Channels are inspired by and works exactly as the channels in [Go](http://golang.org/doc/effective_go.html#concurrency). In general, they allow to pass arbitrary objects from one asynchronous method to another via capped buffer and using 'await' instead of blocking. It is similar to passing items via BlockingCollection, but without blocking.

This example shows how it works:

```C#
using NChannels;

...

private static async Task Echo()
{
	//First, we create a new channel.
	var chan = new Chan<string>();

	Func<Task> reader = async () =>
	{
		string line;

		//The reader acquires some data item by item reading
		//some IO source asynchronously.
		while ((line = await Console.In.ReadLineAsync()).Length > 0)
		{
			//It sends items via the channel waiting until
			//the item is received or put to the buffer.
			//
			//This call may potentially block forever if no task
			//is receiving the items from another end.
			await chan.Send(line);
		}

		//Finally, the reader closes the channel that indicates that
		//no more items will be sent.
		chan.Close();
	};

	Func<Task> writer = async () =>
	{
		ChanResult<string> result;

		//The reader receives the items one by one checking
		//whether an item is received or the channel has been closed.
		while ((result = await chan.Receive()).IsSuccess)
		{
			//Then, it processes an item asynchronously.
			await Console.Out.WriteLineAsync(result.Result);
		}
	};

	await Task.WhenAll(reader(), writer());
}
```

This implementation is simple, straightforward, space efficient (only three items at maximum are reachable at any time) and the entire operation may be performed by a single thread.

By using Select utility, the channels can be multiplexed i.e. a single task can wait for multiple channels simultaneously. 

```C#
using NChannels;

...

private static async void Merge<T>(Chan<T> left, Chan<T> right, Chan<T> result)
{
	var leftClosed = false;
	var rightClosed = false;

	while (!(leftClosed && rightClosed))
	{
		//Select returns when either of the cases obtained a result.
		await new Select()
			.Case(left, async (item, ok) => //The first item is the item,
			{                               //the second is the flag indicating
				if (ok)                     //whether an item has been received.
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
```

Channels doesn't support waiting timeout by themselves. Instead, NChannels namespace provides extension method for TimeSpan returning a channel that emits the current time after the delay represented by the TimeSpan instance. In conjunction with Select, this method can be used to implement timeouts.

```C#
private static async Task ReceiveOrTimeout()
{
	await new Select()
		.Case(GetSomething(), (result, ok) =>
		{
			Console.WriteLine("Got result.");
		})
		//This case will fire if the no other case has fired in 1 second.
		.Case(TimeSpan.FromSeconds(1).After(), (t, ok) =>
		{
			Console.WriteLine("Timeout.");
		})
		.End();
}
```

Like IEnumerable, channels provide a number of sequence-processing operations which allow to easily build quite complex data flows.

```C#
private static void MergeProcessSpread<TIn, TOut>(IEnumerable<Chan<TIn>> input, IEnumerable<Chan<TOut>> targets)
{
	var output = new Chan<TOut>();

	input
		.Merge() // Merging items from multiple channels into a single channel.
		.Where(item => Filter(item)) //Filtering the items.
		.Select<TIn, TOut>(item => Map(item)) //Transforming the items.
		.ForEach(async item =>
		{
			//Performing some actions for each item.
			//...
			await output.Send(item);
		})
		.ContinueWith(task => output.Close());

	//Propagating items from a single channel to multiple channels.
	output.Spread(targets);
}
```