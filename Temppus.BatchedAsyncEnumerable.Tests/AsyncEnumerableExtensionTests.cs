using System.Diagnostics;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Temppus.BatchedAsyncEnumerable.Tests
{
    public class AsyncEnumerableExtensionTests(ITestOutputHelper output)
    {
        private const int SourceItemsCount = 10_000;

        private static readonly IEnumerable<int> SourceItems = Enumerable.Range(0, SourceItemsCount);

        [Fact]
        public async Task Test_Example_Batch_Usage()
        {
            const int batchSize = 5;

            output.WriteLine("Start");

            await foreach (var batch in SampleUnderlyingAsyncEnumerable(itemsToGenerate: 12, output)
                               .ToBatchedAsyncEnumerable(batchSize,
                               TimeSpan.FromMilliseconds(500),
                               CancellationToken.None))
            {
                output.WriteLine($"{DateTime.UtcNow.TimeOfDay:g} - Processing batch of size {batch.Length} for 1 second simulated");
                await Task.Delay(TimeSpan.FromMilliseconds(1000));
                output.WriteLine($"{DateTime.UtcNow.TimeOfDay:g} - Batch of {batch.Length} processed");
            }

            output.WriteLine("Done");
            return;

            static async IAsyncEnumerable<int> SampleUnderlyingAsyncEnumerable(int itemsToGenerate, ITestOutputHelper output)
            {
                foreach (var sourceItem in Enumerable.Range(0, itemsToGenerate))
                {
                    await Task.Delay(1);
                    output.WriteLine($"{DateTime.UtcNow.TimeOfDay:g} - Reading underlying item {sourceItem + 1}");
                    yield return sourceItem;
                }
            }
        }

        [Theory]
        [InlineData(100, 20)]
        [InlineData(100, 100)]
        [InlineData(100, 150)]
        public async Task Test_Correct_Batching(int batchTimerPeriodMs, int batchProcessingTimeMs)
        {
            int batchesCount = 0;
            var flattenItems = new List<int>();

            const int batchSize = 1000;
            await foreach (var batch in CreateUnderlyingAsyncEnumerable(true).ToBatchedAsyncEnumerable(
                               batchSize,
                               TimeSpan.FromMilliseconds(batchTimerPeriodMs),
                               CancellationToken.None))
            {
                var logMessage = $"Batch size: {batch.Length}. From: {batch.First()}, To: {batch.Last()}";
                Debug.WriteLine(logMessage);
                output.WriteLine(logMessage);

                flattenItems.AddRange(batch);
                batchesCount++;

                await Task.Delay(TimeSpan.FromMilliseconds(batchProcessingTimeMs));
            }

            Assert.Equal(SourceItemsCount / batchSize, batchesCount);
            Assert.True(flattenItems.SequenceEqual(SourceItems));
        }

        [Theory(Timeout = 5000)]
        [InlineData(AsyncTermination.Break)]
        [InlineData(AsyncTermination.Cancellation)]
        [InlineData(AsyncTermination.ThrowingException)]
        public async Task Test_Batching_Termination(AsyncTermination asyncTermination)
        {
            int batchesCount = 0;

            const int batchesToCache = 4;
            const int maxBatches = 3;
            const int batchSize = 1000;
            try
            {
                var cts = new CancellationTokenSource();

                await foreach (var batch in CreateUnderlyingAsyncEnumerable().ToBatchedAsyncEnumerable(
                                   batchSize, batchSize * batchesToCache,
                                   TimeSpan.FromMilliseconds(100),
                                   cts.Token))
                {
                    var logMessage = $"Batch size: {batch.Length}. From: {batch.First()}, To: {batch.Last()}";
                    Debug.WriteLine(logMessage);
                    output.WriteLine(logMessage);

                    batchesCount++;

                    await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);

                    if (batchesCount >= maxBatches)
                    {
                        bool @break = false;

                        switch (asyncTermination)
                        {
                            case AsyncTermination.Break:
                                @break = true;
                                break;
                            case AsyncTermination.Cancellation:
                                cts.Cancel();
                                break;
                            case AsyncTermination.ThrowingException:
                                throw new InvalidOperationException("UPS");
                            default:
                                throw new NotImplementedException();
                        }

                        if (@break)
                        {
                            break;
                        }
                    }
                }
            }
            catch (InvalidOperationException e) when (e.Message == "UPS") { }

            if (asyncTermination == AsyncTermination.Cancellation)
            {
                Assert.True(batchesCount >= maxBatches);
            }
            else
            {
                Assert.Equal(maxBatches, batchesCount);
            }
        }

        [Fact]
        public async Task Test_Underlying_Enumerable_Throws_And_Propagates()
        {
            const int batchSize = 10;
            const int failOnIdx = 25;

            var ex = await Assert.ThrowsAsync<Exception>(async () =>
            {
                await foreach (var _ in AsyncEnumerableWhichFailsAfter(failOnIdx)
                                   .ToBatchedAsyncEnumerable(batchSize,
                                       TimeSpan.FromMilliseconds(100),
                                       CancellationToken.None))
                {

                }
            });

            Assert.Equal($"Simulated underlying exception at idx {failOnIdx}", ex.Message);
        }

        private static async IAsyncEnumerable<int> CreateUnderlyingAsyncEnumerable(bool itemsAlwaysAvailableSynchronously = false)
        {
            foreach (var sourceItem in SourceItems)
            {
                if (itemsAlwaysAvailableSynchronously)
                {
                    yield return sourceItem;
                }
                else
                {
                    // Simulate also async path (items is not yielded synchronously)
                    if (sourceItem % 100 == 0)
                    {
                        await Task.Delay(1);
                    }

                    yield return sourceItem;
                }
            }
        }

        private static async IAsyncEnumerable<int> AsyncEnumerableWhichFailsAfter(int failOnIdx = 3)
        {
            int idx = 0;

            while (true)
            {
                await Task.Delay(1);

                if (idx == failOnIdx)
                {
                    throw new Exception($"Simulated underlying exception at idx {failOnIdx}");
                }

                yield return idx++;
            }
        }
    }
}