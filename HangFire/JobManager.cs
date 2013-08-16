﻿using System;
using System.Threading;

using ServiceStack.Logging;

namespace HangFire
{
    public class JobManager : IDisposable
    {
        private readonly Thread _managerThread;
        private readonly JobDispatcherPool _pool;
        private readonly JobSchedulePoller _schedule;
        private readonly RedisClient _client = new RedisClient();

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _disposed;

        private readonly ILog _logger = LogManager.GetLogger("HangFire.Manager");

        public JobManager(int concurrency)
        {
            _managerThread = new Thread(Work)
                {
                    Name = "HangFire.Manager", 
                    IsBackground = true
                };

            _pool = new JobDispatcherPool(concurrency);
            _managerThread.Start();

            _logger.Info("Manager thread has been started.");

            _schedule = new JobSchedulePoller();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _schedule.Dispose();

            _logger.Info("Stopping manager thread...");
            _cts.Cancel();
            _managerThread.Join();

            _pool.Dispose();
            _cts.Dispose();
            _client.Dispose();
        }

        private void Work()
        {
            try
            {
                while (true)
                {
                    var dispatcher = _pool.TakeFree(_cts.Token);

                    _client.TryToDo(
                        redis =>
                        {
                            string job;

                            do
                            {
                                job = redis.BlockingDequeueItemFromList("hangfire:queue:default", TimeSpan.FromSeconds(1));
                                if (job == null && _cts.IsCancellationRequested)
                                {
                                    throw new OperationCanceledException();
                                }
                            } while (job == null);

                            dispatcher.Process(job);
                        });
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Shutdown has been requested. Exiting...");
            }
            catch (Exception ex)
            {
                _logger.Fatal("Unexpected exception caught in the manager thread. Jobs will not be processed.", ex);
            }
        }
    }
}