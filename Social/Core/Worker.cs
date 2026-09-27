using System;
using System.Collections.Generic;
using System.Threading;

namespace TavernEssentials.Social
{
    internal static class Worker
    {
        internal delegate object Job();
        internal delegate void Then(object result, Exception error);

        sealed class Finished
        {
            internal Then Then;
            internal object Result;
            internal Exception Error;
        }

        static readonly object Gate = new object();
        static readonly Queue<Finished> Done = new Queue<Finished>();
        static int _running;

        internal static int Running
        {
            get { lock (Gate) { return _running; } }
        }

        internal static void Run(string name, Job job, Then then)
        {
            lock (Gate) { _running++; }
            Thread t = new Thread(delegate()
            {
                Finished f = new Finished();
                f.Then = then;
                try { f.Result = job(); }
                catch (Exception e) { f.Error = e; }
                lock (Gate)
                {
                    _running--;
                    Done.Enqueue(f);
                }
            });
            t.IsBackground = true;
            t.Name = "TavernEssentials " + name;
            t.Start();
        }

        internal static int Pump()
        {
            int ran = 0;
            while (true)
            {
                Finished f;
                lock (Gate)
                {
                    if (Done.Count == 0) break;
                    f = Done.Dequeue();
                }
                ran++;
                if (f.Then == null) continue;
                try { f.Then(f.Result, f.Error); }
                catch (Exception e) { Log.Error("a background job's follow-up failed: " + e); }
            }
            return ran;
        }
    }
}
