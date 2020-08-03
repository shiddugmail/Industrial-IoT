// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.IIoT.OpcUa.Edge.Publisher.Utils {
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Reactive.Subjects;
    using Serilog;

    /// <summary>
    /// Simple gated observable
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class ObservableWithBackpressure<T> : IObservable<T> {

        /// <inheritdoc/>
        public bool Closed => _dam != null;

        /// <summary>
        /// Create
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="lower"></param>
        /// <param name="upper"></param>
        public ObservableWithBackpressure(ILogger logger, long lower = 3, long upper = 50) {
            _subject = new Subject<T>();
            _lock = new SemaphoreSlim(1);
            _logger = logger ?? Log.Logger;
            _lower = lower;
            _upper = upper;
        }

        /// <summary>
        /// Add
        /// </summary>
        /// <param name="item"></param>
        public void OnNext(T item) {
            _lock.Wait(); // Synchronize
            _subject.OnNext(item); // Enqueue, then block on exit to drain
            _lock.Release();
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<T> observer) {
            return ((IObservable<T>)_subject).Subscribe(observer);
        }

        /// <summary>
        /// Fill one
        /// </summary>
        public void OnFilled(long size) {
            if (!Closed) {
                _logger.Information("Fill at {level}", size);
                if (size >= _upper) {
                    _dam = Task.Factory.StartNew(() => WaitDrain());
                }
            }
        }

        /// <summary>
        /// Drain one
        /// </summary>
        public void OnDrained(long size) {
            if (Closed) {
                _logger.Information("Drain at {level}", size);
                if (size <= _lower) {
                    _reset.Set(); // Unlock write lock
                }
            }
        }

        /// <summary>
        /// Wait to drain
        /// </summary>
        private void WaitDrain() {
            _lock.Wait(); // Aquire lock to block enqueue
            _logger.Information("Stop input");
            _reset.WaitOne(); // Wait for reset
            _dam = null; // remove the dam and continue consuming
            _logger.Information("Resume input");
            _lock.Release();
        }

        private volatile Task _dam;
        private readonly AutoResetEvent _reset = new AutoResetEvent(false);
        private readonly Subject<T> _subject;
        private readonly SemaphoreSlim _lock;
        private readonly ILogger _logger;
        private readonly long _lower;
        private readonly long _upper;
    }
}
