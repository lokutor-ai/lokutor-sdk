using System;
using System.Collections.Concurrent;

namespace Lokutor
{
    /// <summary>
    /// Delivers events on your game's thread: they are queued as the network brings them and raised when you call
    /// <see cref="Update"/>, once per frame. Set <see cref="Dispatcher"/> to deliver them some other way.
    /// </summary>
    public abstract class EventPump
    {
        readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();

        /// <summary>Null (the default): events wait for Update().</summary>
        public Action<Action> Dispatcher;

        /// <summary>Call once per frame from the game's main thread.</summary>
        public virtual void Update()
        {
            while (pending.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { OnCallbackFailed(e); }
            }
        }

        protected void Post(Action a)
        {
            if (Dispatcher != null) Dispatcher(a);
            else pending.Enqueue(a);
        }

        /// <summary>One of your event handlers threw.</summary>
        protected abstract void OnCallbackFailed(Exception e);
    }
}
