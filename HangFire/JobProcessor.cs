﻿using System;
using System.Collections.Generic;
using System.Linq;

using HangFire.Interceptors;

namespace HangFire
{
    class JobProcessor
    {
        private readonly WorkerActivator _activator;
        private readonly IEnumerable<IPerformInterceptor> _interceptors;

        public JobProcessor(WorkerActivator activator, IEnumerable<IPerformInterceptor> interceptors)
        {
            _activator = activator;
            _interceptors = interceptors;
        }

        public void ProcessJob(string serializedJob)
        {
            var job = JsonHelper.Deserialize<Job>(serializedJob);

            using (var worker = _activator.CreateWorker(job.WorkerType))
            {
                worker.Args = job.Args;

                // ReSharper disable once AccessToDisposedClosure
                InvokeInterceptors(worker, worker.Perform);
            }
        }

        private void InvokeInterceptors(Worker worker, Action action)
        {
            var commandAction = action;

            var entries = _interceptors.ToList();
            entries.Reverse();

            foreach (var entry in entries)
            {
                var innerAction = commandAction;
                var currentEntry = entry;

                commandAction = () => currentEntry.InterceptPerform(worker, innerAction);
            }

            commandAction();
        }
    }
}