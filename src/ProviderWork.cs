using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PairTap
{
    internal sealed class ProviderWork
    {
        private const int MaxConcurrent = 8;
        private readonly HashSet<Tuple<IntPtr, uint>> active = new HashSet<Tuple<IntPtr, uint>>();

        internal Task<T> TryStart<T>(IntPtr window, uint process, Func<Task<T>> action)
        {
            var key = Tuple.Create(window, process);
            lock (active)
            {
                if (active.Count >= MaxConcurrent || !active.Add(key)) return null;
            }
            return Task.Run(async () =>
            {
                try { return await action().ConfigureAwait(false); }
                finally { lock (active) active.Remove(key); }
            });
        }
    }
}
