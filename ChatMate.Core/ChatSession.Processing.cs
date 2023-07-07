﻿using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ChatMate.Core;

public partial class ChatSession
{
    private readonly BlockingCollection<Func<CancellationToken, ValueTask>> _messageQueue = new();
    private readonly Task _messageQueueProcessTask;
    private readonly CancellationTokenSource _messageQueueCancellationTokenSource = new();
    private readonly SemaphoreSlim _processingSemaphore = new(0);

    private void Enqueue(Func<CancellationToken, ValueTask> fn)
    {
        try
        {
            _messageQueue.Add(fn, _messageQueueCancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StopProcessingQueue()
    {
        _messageQueueCancellationTokenSource.Cancel();
        await _messageQueueProcessTask;
    }

    private async Task ProcessQueueAsync(CancellationToken token)
    {
        try
        {
            foreach (var message in _messageQueue.GetConsumingEnumerable(token))
            {
                _processingSemaphore.Release();
                try
                {
                    await message(token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exc)
                {
                    _logger.LogError(exc, "Error processing message {MessageType}", message.GetType().Name);
                }
                finally
                {
                    await _processingSemaphore.WaitAsync(token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _messageQueue.Dispose();
        }
    }
    
    public async Task WaitForPendingQueueItemsAsync()
    {
        while (_messageQueue.Count > 0 || _processingSemaphore.CurrentCount > 0)
        {
            await Task.Delay(10);
        }
    }
}
