﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NAPS2.Scan.Images;

namespace NAPS2.Scan.Wia
{
    /// <summary>
    /// Manages a separate Windows Forms event loop to allow WIA interaction to be performed asynchronously.
    /// </summary>
    public class WiaBackgroundEventLoop : IDisposable
    {
        private readonly ExtendedScanSettings settings;
        private readonly ScanDevice scanDevice;
        private readonly IScannedImageFactory scannedImageFactory;

        private readonly AutoResetEvent initWaiter = new AutoResetEvent(false);
        private Thread thread;
        private Form form;
        private WiaState wiaState;

        public WiaBackgroundEventLoop(ExtendedScanSettings settings, ScanDevice scanDevice, IScannedImageFactory scannedImageFactory)
        {
            this.settings = settings;
            this.scanDevice = scanDevice;
            this.scannedImageFactory = scannedImageFactory;

            thread = new Thread(RunEventLoop);
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            // Wait for the thread to initialize the background form and event loop
            initWaiter.WaitOne();
        }

        public void DoSync(Action<WiaState> action)
        {
            form.Invoke(Bind(action));
        }

        public T GetSync<T>(Func<WiaState, T> action)
        {
            T value = default(T);
            form.Invoke(Bind(wia =>
            {
                value = action(wia);
            }));
            return value;
        }

        public void DoAsync(Action<WiaState> action)
        {
            form.BeginInvoke(Bind(action));
        }

        public void Dispose()
        {
            if (thread != null)
            {
                DoSync(wia => Application.ExitThread());
                thread = null;
            }
        }

        private Action Bind(Action<WiaState> action)
        {
            return () =>
            {
                if (wiaState == null)
                {
                    wiaState = InitWia();
                }
                action(wiaState);
            };
        }

        private WiaState InitWia()
        {
            var device = WiaApi.GetDevice(scanDevice);
            var item = WiaApi.GetItem(device, settings);
            return new WiaState(device, item);
        }

        private void RunEventLoop()
        {
            form = new Form
            {
                WindowState = FormWindowState.Minimized,
                ShowInTaskbar = false
            };
            form.Load += form_Load;
            Application.Run(form);
        }

        private void form_Load(object sender, EventArgs e)
        {
            initWaiter.Set();
        }
    }
}