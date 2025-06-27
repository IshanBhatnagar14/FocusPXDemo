using Caliburn.Micro;
using FPXDemo.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FPXDemo.ViewModels
{
    public class ShellViewModel : Screen
    {
        public DeviceModel DeviceModel { get; private set; }
        public PlotModel PlotModel { get; private set; }
        public LineSeries LineSeries { get; private set; }

        private string _serialNumber;
        private string _ipAddress;
        private string _beamSetName;
        private string _logging;

        private CancellationTokenSource _cts;

        public ShellViewModel()
        {
            SetupPlot();
        }

        private void SetupPlot()
        {
            PlotModel = new PlotModel { Title = "A-Scan Live Plot" };

            var xAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Time",
                MajorGridlineStyle = LineStyle.Solid
            };
            PlotModel.Axes.Add(xAxis);

            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Amplitude",
                MajorGridlineStyle = LineStyle.Solid
            };
            PlotModel.Axes.Add(yAxis);

            LineSeries = new LineSeries
            {
                Title = "A-Scan",
                Color = OxyColors.Blue,
                StrokeThickness = 1.5
            };
            PlotModel.Series.Add(LineSeries);
        }

        public async Task ConnectDevice()
        {
            DeviceModel = await Task.Run(() => new DeviceModel());
            SerialNumber = DeviceModel.device.GetInfo().GetSerialNumber();
            IPAddress = DeviceModel.device.GetInfo().GetAddressIPv4();
            Log("Device connected.");
        }

        public void CreateBeamSets()
        {
            DeviceModel.CreateBeamSetsFromLawFiles();
            BeamSetName = "Lprobe & Rprobe loaded";
            Log("BeamSets created and loaded.");
        }

        public void SetupAcquisition()
        {
            if (DeviceModel.SetupAcquisition())
                Log("Acquisition setup done.");
            else
                Log("Acquisition setup failed.");
        }

        public async Task StartAscanLoop()
        {
            if (DeviceModel == null)
            {
                Log("DeviceModel not ready.");
                return;
            }

            _cts = new CancellationTokenSource();

            Log("Starting A-Scan loop...");
            await Task.Run(() => DeviceModel.StartAscanLoopAsync(_cts.Token));

            _ = Task.Run(() => UpdatePlotLoop(_cts.Token)); // Run plot updater in parallel
        }

        public void StopAscanLoop()
        {
            _cts?.Cancel();
            DeviceModel?.StopAscanLoop();
            Log("A-Scan loop stopped.");
        }

        private async Task UpdatePlotLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (DeviceModel.AscanFrames.Any())
                {
                    var frame = DeviceModel.AscanFrames.First(); // Take the first frame
                    LineSeries.Points.Clear();

                    for (int i = 0; i < frame.Signal.Length; i++)
                    {
                        LineSeries.Points.Add(new DataPoint(frame.TimeAxis[i], frame.Signal[i]));
                    }

                    PlotModel.InvalidatePlot(true);
                }

                await Task.Delay(50, token);
            }
        }

        private void Log(string message)
        {
            Logging += $"{DateTime.Now:HH:mm:ss} → {message}{Environment.NewLine}";
        }

        // ✅ Bindable props

        public string SerialNumber
        {
            get => _serialNumber;
            set { _serialNumber = value; NotifyOfPropertyChange(() => SerialNumber); }
        }

        public string IPAddress
        {
            get => _ipAddress;
            set { _ipAddress = value; NotifyOfPropertyChange(() => IPAddress); }
        }

        public string BeamSetName
        {
            get => _beamSetName;
            set { _beamSetName = value; NotifyOfPropertyChange(() => BeamSetName); }
        }

        public string Logging
        {
            get => _logging;
            set { _logging = value; NotifyOfPropertyChange(() => Logging); }
        }
    }
}
