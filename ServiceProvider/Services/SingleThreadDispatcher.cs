using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceProvider.Services
{
    /// <summary>
    /// COM objects, especially audio and MIDI devices requires this.
    /// Thread that makes them must be the the one who destroys.
    /// </summary>
    internal class SingleThreadDispatcher
    {
        Thread executor;
        AutoResetEvent execute = new AutoResetEvent(false);
        ConcurrentQueue<Action> actions = new ConcurrentQueue<Action>();
        int disposed = 0;
        bool noThrow;
        public SingleThreadDispatcher(bool noThrow = true)
        {
            this.noThrow = noThrow;
            executor = new Thread(ExecutionLoop);
            executor.Name = "CustomDispatcher";
            executor.Start();
        }
        
        private void ExecutionLoop()
        {
            while (true)
            {
                try
                {
                    execute.WaitOne();
                }
                catch { }

                if (Interlocked.CompareExchange(ref disposed, 0, 0) == 1)
                    return;

                while (actions.TryDequeue(out var todo))
                {
                    try
                    {
                        todo?.Invoke();

                    }
                    catch
                    {
                        if (!noThrow)
                            throw;
                    }
                }
            }
        }

        public void Enqueue(Action todo)
        {
            actions.Enqueue(todo);
            execute.Set();

        }
        public void EnqueueBlocking(Action todo, int timeout=0)
        {
            ManualResetEvent mre = new ManualResetEvent(false); 
            actions.Enqueue(() => 
            {
                try
                {
                    todo?.Invoke();

                }
                finally
                {
                    try
                    {
                        mre.Set();
                    }
                    catch{ }
                }
            });
            execute.Set();
            if(Thread.CurrentThread.ManagedThreadId == executor.ManagedThreadId)
            {
                return;
            }
            _=timeout == 0 ? mre.WaitOne(): mre.WaitOne(timeout);


        }

        internal void Dispose()
        {
           Interlocked.Exchange(ref disposed, 1);
        }
    }
}
