# NChannels

NChannels is an implementation of asynchronous channels (like in Go) designed for use in C# with async/await.

## Usage

```C#
using System;
using System.Threading.Tasks;
using NChannels;

...

public void SendReceive()
{
	var chan = new Chan<int>(); // or 'new Chan<int>(n)' 
								// to specify the buffer size (default is 1).

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
	collect().Wait();
}
```