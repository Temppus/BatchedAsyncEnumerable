using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;

namespace Temppus.BatchedAsyncEnumerable
{
    public static class AsyncEnumerableExtensions
    {
        /// <summary>
        /// Batch async stream so that when one batch is ready and yielded, second batch can be enumerated asynchronously.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="asyncEnumerable"></param>
        /// <param name="batchSize"></param>
        /// <param name="batchTimeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static IAsyncEnumerable<T[]> ToBatchedAsyncEnumerable<T>(
            this IAsyncEnumerable<T> asyncEnumerable,
            int batchSize,
            TimeSpan batchTimeout,
            CancellationToken cancellationToken)
        {
            return ToBatchedAsyncEnumerable(asyncEnumerable, batchSize, batchSize, batchTimeout, cancellationToken);
        }

        /// <summary>
        /// Batch async stream with provided so that when one batch is ready and yielded, second batch can be enumerated asynchronously.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="asyncEnumerable"></param>
        /// <param name="batchSize"></param>
        /// <param name="boundedCapacity"></param>
        /// <param name="batchTimeout"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async IAsyncEnumerable<T[]> ToBatchedAsyncEnumerable<T>(
            this IAsyncEnumerable<T> asyncEnumerable,
            int batchSize,
            int boundedCapacity,
            TimeSpan batchTimeout,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var batchBlock = new BatchBlock<T>(batchSize, new GroupingDataflowBlockOptions
            {
                BoundedCapacity = boundedCapacity,
            });

            var timer = new System.Timers.Timer(batchTimeout.TotalMilliseconds)
            {
                AutoReset = true,
                Enabled = true
            };

            timer.Elapsed += (s, e) => batchBlock.TriggerBatch();

            var batchingCts = new CancellationTokenSource();
            var joinCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var messageReadingTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in asyncEnumerable.WithCancellation(joinCts.Token))
                    {
                        timer.Stop();
                        await batchBlock.SendAsync(message, joinCts.Token);
                        // make sure that timer is re/started only after sending message to batch block
                        // otherwise there could be cases when batchBlock is currently full thus SendAsync call is blocked
                        // reading thread yields to caller and caller batch processing time can be non-trivial and took longer time than period of timer
                        // in that case we do not want to trigger any batches meanwhile
                        timer.Start();
                    }

                    batchBlock.Complete();
                    await batchBlock.Completion;
                }
                catch (OperationCanceledException)
                {
                    // not really necessary but for good manners
                    batchBlock.Complete();
                }
                finally
                {
                    batchingCts.Cancel();
                }
            }, CancellationToken.None);

            try
            {
                while (true)
                {
                    T[] messagesBatch = null;

                    try
                    {
                        messagesBatch = await batchBlock.ReceiveAsync(batchingCts.Token);
                    }
                    // This can only happen when client cancelled reading by signalling cancellation
                    catch (OperationCanceledException)
                    {
                        batchBlock.Complete();
                        timer.Stop();
                    }
                    // ReceiveAsync can throw InvalidOperationException when block is completed
                    catch (InvalidOperationException)
                    {
                        timer.Stop();
                    }
                    // This should never happen, but to be bulletproof handle this also
                    catch (Exception)
                    {
                        try
                        {
                            joinCts.Cancel();
                            await messageReadingTask;
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        finally
                        {
                            batchBlock.Complete();
                            timer.Stop();
                        }
                    }

                    if (messagesBatch == null)
                    {
                        break;
                    }

                    if (messagesBatch.Length != 0)
                    {
                        yield return messagesBatch;
                    }
                }
            }
            finally
            {
                joinCts.Cancel();

                // Here we either
                // 1.finished reading all batches
                // 2. reading was cancelled
                // 3. exception was thrown by caller while iterating over batch
                // In all cases we need to make sure that batch block will not contain any messages
                // so that waiting for completion task will always end properly
                batchBlock.Complete();
                batchBlock.TryReceiveAll(out _);

                await messageReadingTask;
            }
        }
    }
}