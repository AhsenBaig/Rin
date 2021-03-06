﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rin.Core.Event
{
    public class MessageEventBus<T> : IMessageEventBus<T>
    {
        private IMessageSubscriber<T>[] _subscribers = Array.Empty<IMessageSubscriber<T>>();
        private System.Threading.Channels.Channel<T> _channel;
        private Task _readerTask;
        private bool _disposed;
        private CancellationTokenSource _cancellationTokenSource;

        public MessageEventBus()
        {
            _channel = System.Threading.Channels.Channel.CreateUnbounded<T>(new System.Threading.Channels.UnboundedChannelOptions()
            {
                SingleReader = true,
            });
            _readerTask = Task.CompletedTask;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Subscribe(IEnumerable<IMessageSubscriber<T>> subscribers)
        {
            if (_readerTask != Task.CompletedTask)
            {
                throw new InvalidOperationException("MessageEventBus was already started.");
            }

            _subscribers = subscribers.ToArray();
            _readerTask = Task.Run(RunLoopAsync);
        }

        private async Task RunLoopAsync()
        {
            var reader = _channel.Reader;
            while (await reader.WaitToReadAsync(_cancellationTokenSource.Token) && !_cancellationTokenSource.IsCancellationRequested)
            {
                var item = await reader.ReadAsync(_cancellationTokenSource.Token);

                foreach (var subscriber in _subscribers)
                {
                    try
                    {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        subscriber.Publish(item).ContinueWith(x => { }, TaskContinuationOptions.OnlyOnFaulted);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    }
                    catch (Exception ex)
                    {
#if DEBUG
                        Console.WriteLine(ex);
#endif
                    }
                }
            }
        }

        public async ValueTask PostAsync(T item)
        {
            await _channel.Writer.WaitToWriteAsync(_cancellationTokenSource.Token);
            await _channel.Writer.WriteAsync(item, _cancellationTokenSource.Token);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cancellationTokenSource.Cancel();
            _readerTask.Wait();

            _disposed = true;
        }
    }
}
