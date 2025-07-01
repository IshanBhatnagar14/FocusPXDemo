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
using System.IO;
using System.Diagnostics;

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
            try
            {
                IDeviceConfiguration deviceConfiguration = device.GetConfiguration();
                ultrasoundConfiguration = deviceConfiguration.GetUltrasoundConfiguration();
                digitizerTechnology = ultrasoundConfiguration.GetDigitizerTechnology(UltrasoundTechnology.PhasedArray);
                IBeamSetFactory beamSetFactory = digitizerTechnology.GetBeamSetFactory();

                // Load Lprobe
                var lProbeFormations = beamSetFactory.CreateBeamFormationCollectionFromLawFile("config1.law");
                if (lProbeFormations == null)
                    throw new FileNotFoundException("Could not find or open 'config1.law'. Please check the file path.");

                var lProbeBeamSet = beamSetFactory.CreateBeamSetPhasedArray("Lprobe", lProbeFormations);
                lProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanDataSize(IAmplitudeSettings.AscanDataSize.EightBits);
                lProbeBeamSet.GetDigitizingSettings().GetTimeSettings().SetAscanCompressionFactor(5);
                lProbeBeamSet.GetPulsingSettings().SetAscanAveragingFactor(IPulsingSettings.AveragingFactor.One);
                lProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetScalingType(IAmplitudeSettings.ScalingType.Linear);
                lProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanRectification(IAmplitudeSettings.RectificationType.Full);

                // Load Rprobe
                var rProbeFormations = beamSetFactory.CreateBeamFormationCollectionFromLawFile("config2.law");
                if (rProbeFormations == null)
                    throw new FileNotFoundException("Could not find or open 'config2.law'. Please check the file path.");

                var rProbeBeamSet = beamSetFactory.CreateBeamSetPhasedArray("Rprobe", rProbeFormations);
                rProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanDataSize(IAmplitudeSettings.AscanDataSize.EightBits);
                rProbeBeamSet.GetDigitizingSettings().GetTimeSettings().SetAscanCompressionFactor(5);
                rProbeBeamSet.GetPulsingSettings().SetAscanAveragingFactor(IPulsingSettings.AveragingFactor.One);
                rProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetScalingType(IAmplitudeSettings.ScalingType.Linear);
                rProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanRectification(IAmplitudeSettings.RectificationType.Full);

                // Bind to connector
                IConnector connectorPA = digitizerTechnology.GetConnectorCollection().GetConnector(0);
                ultrasoundConfiguration.GetFiringBeamSetCollection().Add(lProbeBeamSet, connectorPA);
                ultrasoundConfiguration.GetFiringBeamSetCollection().Add(rProbeBeamSet, connectorPA);

                LprobeBeamSet = lProbeBeamSet;
                RprobeBeamSet = rProbeBeamSet;

                MessageBox.Show("Beamsets Lprobe and Rprobe successfully loaded from law files.");
            }
            catch (FileNotFoundException fnfEx)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] {fnfEx.Message}");
                MessageBox.Show(fnfEx.Message, "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Unexpected: {ex.Message}");
                MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public double PulserVoltage
        {
            get => digitizerTechnology?.GetPulserVoltage() ?? 0;
            set { digitizerTechnology?.SetPulserVoltage(value); }
        }

        public bool ValidateBeforeAcquisition()
        {
            try
            {
                if (device == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ERROR] Device handle is null.");
                    return false;
                }

                var info = device.GetInfo();
                if (info == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ERROR] Device info could not be retrieved.");
                    return false;
                }

                string serial = info.GetSerialNumber();
                string ip = info.GetAddressIPv4();
                System.Diagnostics.Debug.WriteLine($"[INFO] Device Info → Serial: {serial}, IP: {ip}");

                if (ultrasoundConfiguration == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ERROR] Ultrasound configuration is null.");
                    return false;
                }

                if (digitizerTechnology == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ERROR] DigitizerTechnology is null.");
                    return false;
                }

                var connectorCollection = digitizerTechnology.GetConnectorCollection();
                if (connectorCollection == null || connectorCollection.GetCount() == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[ERROR] No connectors found on digitizer technology.");
                    return false;
                }

                if (LprobeBeamSet == null || RprobeBeamSet == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ERROR] Beamsets are not created. Call CreateBeamSetsFromLawFiles() first.");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("[INFO] All device sanity checks passed.");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXCEPTION] Validation failed: {ex.Message}");
                return false;
            }
        }

        public bool SetupAcquisition()
        {
            if (!ValidateBeforeAcquisition())
            {
                System.Diagnostics.Debug.WriteLine("[ERROR] Device validation failed. Aborting acquisition setup.");
                return false;
            }

            try
            {
                // Dispose previous acquisition if any
                if (acquisition != null)
                {
                    System.Diagnostics.Debug.WriteLine("[INFO] Disposing previous acquisition instance.");
                    acquisition.Dispose();
                    acquisition = null;
                }

                acquisition = IAcquisition.CreateEx(device);
                System.Diagnostics.Debug.WriteLine("[INFO] Acquisition object created successfully.");

                acquisition.SetFiringTrigger(IAcquisition.FiringTrigger.Internal);
                acquisition.SetRate(60);
                acquisition.ApplyConfiguration();
                acquisition.Start();

                System.Diagnostics.Debug.WriteLine("[INFO] Acquisition started successfully.");
                return true;
            }
            catch (AccessViolationException ave)
            {
                System.Diagnostics.Debug.WriteLine($"[CRITICAL] Access violation: {ave.Message}");
                MessageBox.Show("Access violation in native driver. Please check hardware state.", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] SetupAcquisition failed: {ex.Message}");
                MessageBox.Show($"SetupAcquisition failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        // ------------------------
        // Universal Beam Lookup (for GET)
        // ------------------------

        public IBeam GetBeamByGlobalIndex(uint globalBeamIndex)
        {
            if (LprobeBeamSet == null || RprobeBeamSet == null)
                throw new InvalidOperationException("BeamSets must be created first.");

            if (globalBeamIndex < LprobeBeamSet.GetBeamCount())
                return LprobeBeamSet.GetBeam(globalBeamIndex);
            else
            {
                uint rightBeamIndex = globalBeamIndex - LprobeBeamSet.GetBeamCount();
                if (rightBeamIndex < RprobeBeamSet.GetBeamCount())
                    return RprobeBeamSet.GetBeam(rightBeamIndex);
                else
                    throw new ArgumentOutOfRangeException(nameof(globalBeamIndex), "Beam index exceeds total available beams.");
            }
        }

        // ------------------------
        // GETTERS
        // ------------------------

        public double GetAscanStart(uint globalBeamIndex)
        {
            var beam = GetBeamByGlobalIndex(globalBeamIndex);
            double value = beam.GetAscanStart();
            Debug.WriteLine($"[DEBUG] GetAscanStart() → Beam {globalBeamIndex} → {value}");
            return value;
        }

        public double GetGain(uint globalBeamIndex)
        {
            var beam = GetBeamByGlobalIndex(globalBeamIndex);
            double value = beam.GetGain();
            Debug.WriteLine($"[DEBUG] GetGain() → Beam {globalBeamIndex} → {value}");
            return value;
        }

        public uint GetAscanLength(uint globalBeamIndex)
        {
            var beam = GetBeamByGlobalIndex(globalBeamIndex);
            uint value = (uint)beam.GetAscanLength();
            Debug.WriteLine($"[DEBUG] GetAscanLength() → Beam {globalBeamIndex} → {value}");
            return value;
        }

        public uint GetAscanSampleQuantity(uint globalBeamIndex)
        {
            var beam = GetBeamByGlobalIndex(globalBeamIndex);
            uint value = beam.GetAscanSampleQuantity();
            Debug.WriteLine($"[DEBUG] GetAscanSampleQuantity() → Beam {globalBeamIndex} → {value}");
            return value;
        }

        // ------------------------
        // SETTERS: apply to all beams
        // ------------------------

        public void SetAscanLength(uint newLength)
        {
            if (LprobeBeamSet != null)
            {
                for (uint i = 0; i < LprobeBeamSet.GetBeamCount(); i++)
                    LprobeBeamSet.GetBeam(i).SetAscanLength(newLength);
            }

            if (RprobeBeamSet != null)
            {
                for (uint i = 0; i < RprobeBeamSet.GetBeamCount(); i++)
                    RprobeBeamSet.GetBeam(i).SetAscanLength(newLength);
            }
        }

        public void SetAscanStart(double newStart)
        {
            if (LprobeBeamSet != null)
            {
                for (uint i = 0; i < LprobeBeamSet.GetBeamCount(); i++)
                    LprobeBeamSet.GetBeam(i).SetAscanStart(newStart);
            }

            if (RprobeBeamSet != null)
            {
                for (uint i = 0; i < RprobeBeamSet.GetBeamCount(); i++)
                    RprobeBeamSet.GetBeam(i).SetAscanStart(newStart);
            }
        }

        public void SetGain(double newGain)
        {
            if (LprobeBeamSet != null)
            {
                for (uint i = 0; i < LprobeBeamSet.GetBeamCount(); i++)
                    LprobeBeamSet.GetBeam(i).SetGainEx(newGain);
            }

            if (RprobeBeamSet != null)
            {
                for (uint i = 0; i < RprobeBeamSet.GetBeamCount(); i++)
                    RprobeBeamSet.GetBeam(i).SetGainEx(newGain);
            }
        }

    }
}
