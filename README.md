# BatchedAsyncEnumerable
Extension method that will transform `IAsyncEnumerable<T>` to `IAsyncEnumerable<T[]>` allowing for efficient asynchronous batch processing with one line of code.

Nuget available [here](https://www.nuget.org/packages/BatchedAsyncEnumerable/).

https://www.nuget.org/packages/BatchedAsyncEnumerable

## Motivation:
The `IAsyncEnumerable<T>` type is powerful, but using it comes with many pitfalls. Creating a batching method for this type revealed more gotchas than expected, given the nature of the `async` and `yield` combination. These issues are extremely hard to debug and must be handled carefully to avoid shooting yourself in the foot.

## Example usage:

 ```csharp

            await foreach (var batch in SomeAsyncEnumerable()
                               .ToBatchedAsyncEnumerable(batchSize: batchSize,
                               batchTimeout: TimeSpan.FromMilliseconds(500),
                               cancellationToken))
            {
                // process your batch
            }

 ```


## How it works

Given example ([Test_Example_Batch_Usage](Temppus.BatchedAsyncEnumerable.Tests/AsyncEnumerableExtensionTests.cs)) where we set `batchSize` to 5 and `batchTimeout` to 500ms we can see the output od processing.

```
Start
15:14:49.3935344 - Reading underlying item 1
15:14:49.4089621 - Reading underlying item 2
15:14:49.4244730 - Reading underlying item 3
15:14:49.4399959 - Reading underlying item 4
15:14:49.4554766 - Reading underlying item 5
15:14:49.4567448 - Processing batch of size 5 for 1 second simulated
15:14:49.4709696 - Reading underlying item 6
15:14:49.4865186 - Reading underlying item 7
15:14:49.5017162 - Reading underlying item 8
15:14:49.5169993 - Reading underlying item 9
15:14:49.5326119 - Reading underlying item 10
15:14:49.5481148 - Reading underlying item 11
15:14:50.4572927 - Batch of 5 processed
15:14:50.4578585 - Processing batch of size 5 for 1 second simulated
15:14:50.4723612 - Reading underlying item 12
15:14:51.4594741 - Batch of 5 processed
15:14:51.4596284 - Processing batch of size 2 for 1 second simulated
15:14:52.4623563 - Batch of 2 processed
Done
```

YOu can see that while we are processing batch next items are being enumerated asynchronously from underlying enumerable to achieve better throughput. Batch will be yielded either when specified `batchSize` is reached or if `batchTimeout` elapsed.