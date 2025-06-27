using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OlympusNDT.Instrumentation.NET;
using Caliburn.Micro;

namespace FPXDemo.Models
{
    public class DeviceModel : PropertyChangedBase // Caliburn wants this for NotifyOfPropertyChange
    {
        public IDevice device { get; set; }
        public IBeamSet LprobeBeamSet { get; set; }
        public IBeamSet RprobeBeamSet { get; set; }
        public IBeamSet beamSet { get; set; }
        public IUltrasoundConfiguration ultrasoundConfiguration { get; set; }
        public IDigitizerTechnology digitizerTechnology { get; set; }
        public IAcquisition acquisition { get; set; }

        private List<AscanFrame> _ascanFrames = new List<AscanFrame>();
        public List<AscanFrame> AscanFrames
        {
            get => _ascanFrames;
            private set
            {
                _ascanFrames = value;
                NotifyOfPropertyChange(() => AscanFrames);
            }
        }

        private CancellationTokenSource _cts;

        public DeviceModel()
        {
            Utilities.ResolveDependenciesPath();
            int timeout = 5000;
            IDeviceDiscovery deviceDiscovery = IDeviceDiscovery.Create("192.168.0.1");
            DiscoverResult discoverResult = deviceDiscovery.DiscoverFor(timeout);
            device = discoverResult.device;
            if (discoverResult.status != DiscoverResult.Status.DeviceFound)
            {
                MessageBox.Show("Device is not Found!");
                return;
            }
            DownloadFirmwarePackage();
        }

        public void DownloadFirmwarePackage()
        {
            string packageName = "FocusPxPackage-1.3";
            IFirmwarePackageCollection firmwarePackages = IFirmwarePackageScanner.GetFirmwarePackageCollection();
            for (uint i = 0; i < firmwarePackages.GetCount(); i++)
            {
                var pkg = firmwarePackages.GetFirmwarePackage(i);
                if (pkg.GetName().Contains(packageName))
                {
                    device.Start(pkg);
                    break;
                }
            }
        }

        public void CreateBeamSetsFromLawFiles()
        {
            IDeviceConfiguration deviceConfiguration = device.GetConfiguration();
            ultrasoundConfiguration = deviceConfiguration.GetUltrasoundConfiguration();
            digitizerTechnology = ultrasoundConfiguration.GetDigitizerTechnology(UltrasoundTechnology.PhasedArray);
            IBeamSetFactory beamSetFactory = digitizerTechnology.GetBeamSetFactory();

            var lProbeFormations = beamSetFactory.CreateBeamFormationCollectionFromLawFile("config1.law");
            var lProbeBeamSet = beamSetFactory.CreateBeamSetPhasedArray("Lprobe", lProbeFormations);
            lProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanDataSize(IAmplitudeSettings.AscanDataSize.EightBits);
            lProbeBeamSet.GetDigitizingSettings().GetTimeSettings().SetAscanCompressionFactor(5);
            lProbeBeamSet.GetPulsingSettings().SetAscanAveragingFactor(IPulsingSettings.AveragingFactor.One);
            lProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetScalingType(IAmplitudeSettings.ScalingType.Linear);
            lProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanRectification(IAmplitudeSettings.RectificationType.Full);

            var rProbeFormations = beamSetFactory.CreateBeamFormationCollectionFromLawFile("config2.law");
            var rProbeBeamSet = beamSetFactory.CreateBeamSetPhasedArray("Rprobe", rProbeFormations);
            rProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanDataSize(IAmplitudeSettings.AscanDataSize.EightBits);
            rProbeBeamSet.GetDigitizingSettings().GetTimeSettings().SetAscanCompressionFactor(5);
            rProbeBeamSet.GetPulsingSettings().SetAscanAveragingFactor(IPulsingSettings.AveragingFactor.One);
            rProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetScalingType(IAmplitudeSettings.ScalingType.Linear);
            rProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanRectification(IAmplitudeSettings.RectificationType.Full);

            IConnector connectorPA = digitizerTechnology.GetConnectorCollection().GetConnector(0);
            ultrasoundConfiguration.GetFiringBeamSetCollection().Add(lProbeBeamSet, connectorPA);
            ultrasoundConfiguration.GetFiringBeamSetCollection().Add(rProbeBeamSet, connectorPA);

            LprobeBeamSet = lProbeBeamSet;
            RprobeBeamSet = rProbeBeamSet;

            MessageBox.Show("Beamsets Lprobe and Rprobe successfully loaded from law files.");
        }

        public double PulserVoltage
        {
            get => digitizerTechnology?.GetPulserVoltage() ?? 0;
            set { digitizerTechnology?.SetPulserVoltage(value); }
        }

        public bool SetupAcquisition()
        {
            if (device == null) return false;

            try
            {
                acquisition = IAcquisition.CreateEx(device);
                acquisition.SetFiringTrigger(IAcquisition.FiringTrigger.Internal);
                acquisition.SetRate(60);
                acquisition.ApplyConfiguration();
                acquisition.Start();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Acquisition setup failed: {ex.Message}");
                return false;
            }
        }

        private ICycleData WaitForValidCycleData()
        {
            if (acquisition == null)
                return null;

            using (var result = acquisition.WaitForDataEx())
            {
                return result.status == IAcquisition.WaitForDataResultEx.Status.DataAvailable
                    ? result.cycleData
                    : null;
            }
        }


        public async Task StartAscanLoopAsync(CancellationToken token)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            while (!_cts.Token.IsCancellationRequested)
            {
                bool success = await Task.Run(() => CollectAllAscanData(), _cts.Token);
                if (success)
                    System.Diagnostics.Debug.WriteLine($"[INFO] Collected {_ascanFrames.Count} A-scan frames at {DateTime.Now}");
                await Task.Delay(50, _cts.Token); // Adjust as needed
            }
        }

        public void StopAscanLoop()
        {
            _cts?.Cancel();
            System.Diagnostics.Debug.WriteLine("[INFO] A-scan loop cancelled.");
        }

        public bool CollectAllAscanData()
        {
            var cycleData = WaitForValidCycleData();
            if (cycleData == null) return false;

            var ascans = cycleData.GetAscanCollection();
            if (ascans.GetCount() == 0) return false;

            var frames = new List<AscanFrame>();

            for (uint i = 0; i < ascans.GetCount(); i++)
            {
                var ascan = ascans.GetAscan(i);
                int samples = (int)ascan.GetSampleQuantity();
                int[] signal = new int[samples];
                Marshal.Copy(ascan.GetData(), signal, 0, samples);

                var timeRange = ascan.GetTimeDataRange();
                double start = timeRange.GetFloatingMin();
                double stop = timeRange.GetFloatingMax();
                double step = (stop - start) / (samples - 1);
                double[] timeAxis = new double[samples];
                for (int j = 0; j < samples; j++)
                    timeAxis[j] = start + j * step;

                uint beamIndex = ascan.GetBeamFiringOrder();
                IBeam beam = beamIndex < LprobeBeamSet.GetBeamCount()
                    ? LprobeBeamSet.GetBeam(beamIndex)
                    : RprobeBeamSet.GetBeam(beamIndex - LprobeBeamSet.GetBeamCount());

                double gain = beam?.GetGain() ?? 0;

                frames.Add(new AscanFrame
                {
                    BeamIndex = (int)beamIndex,
                    Gain = gain,
                    Signal = signal,
                    TimeAxis = timeAxis
                });
            }

            AscanFrames = frames;
            return true;
        }

        public void ResetAscans()
        {
            AscanFrames = new List<AscanFrame>();
            System.Diagnostics.Debug.WriteLine("[INFO] Cleared A-scan frames.");
        }
    }
}
