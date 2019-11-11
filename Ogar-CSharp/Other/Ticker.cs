﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Ogar_CSharp
{

    public class Ticker
    {
        private HashSet<Action> callbacks = new HashSet<Action>();
        public bool running;
        public int step;
        public Thread tickingThread;
        public Ticker(int step) =>
            this.step = step;
        public void Add(Action callback)
        {
            callbacks.Add(callback);
        }
        public void Remove(Action callback)
        {
            callbacks.Remove(callback);
        }
        public void Start()
        {
            if (running)
                throw new Exception("The ticker has already been started");
            running = true;
            tickingThread = new Thread(TickThread) { IsBackground = true };
            tickingThread.Start();
        }
        private void TickThread()
        {
            while (running)
            {
                foreach (var callback in callbacks)
                {
                    callback();
                }
                Thread.Sleep(step);
            }
        }
        public void Stop()
        {
            if (!running)
                throw new Exception("The ticker hasn't started");
            running = false;
            if (tickingThread != null)
                tickingThread.Join();
        }
    }
}
