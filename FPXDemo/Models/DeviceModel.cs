using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using OlympusNDT.Instrumentation.NET;
using Caliburn.Micro;


namespace FPXDemo.Models
{
    public class DeviceModel
    {
        public IDevice device { get; set; }
        public IBeamSet LprobeBeamSet { get; set; }

        public IBeamSet RprobeBeamSet { get; set; }

        public IBeamSet beamSet { get; set; }
        public IUltrasoundConfiguration ultrasoundConfiguration { get; set; }
        public IDigitizerTechnology digitizerTechnology { get; set; }
        public IAcquisition acquisition { get; set; }


        private string _text1 = "Initial Text";

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
            //MessageBox.Show("Device is Found!");
            DownloadFirmwarePackage();
        }

        public void DownloadFirmwarePackage()
        {
            string packageName = "FocusPxPackage-1.3";
            IFirmwarePackage firmwarePackage;
            IFirmwarePackageCollection firmwarePackages = IFirmwarePackageScanner.GetFirmwarePackageCollection();
            for (uint i=0; i<firmwarePackages.GetCount(); i++)
            {
                if (firmwarePackages.GetFirmwarePackage(i).GetName().Contains(packageName))
                {
                    firmwarePackage = firmwarePackages.GetFirmwarePackage(i);
                    device.Start(firmwarePackage);
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

            // Load Lprobe beamset from config1.law
            var lProbeFormations = beamSetFactory.CreateBeamFormationCollectionFromLawFile("config1.law");
            var lProbeBeamSet = beamSetFactory.CreateBeamSetPhasedArray("Lprobe", lProbeFormations);
            lProbeBeamSet.GetDigitizingSettings()
                 .GetAmplitudeSettings()
                 .SetAscanDataSize(IAmplitudeSettings.AscanDataSize.EightBits);

            lProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetScalingType(IAmplitudeSettings.ScalingType.Linear);
            lProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanRectification(IAmplitudeSettings.RectificationType.Full);
            // Load Rprobe beamset from config2.law
            var rProbeFormations = beamSetFactory.CreateBeamFormationCollectionFromLawFile("config2.law");
            var rProbeBeamSet = beamSetFactory.CreateBeamSetPhasedArray("Rprobe", rProbeFormations);
            rProbeBeamSet.GetDigitizingSettings()
                 .GetAmplitudeSettings()
                 .SetAscanDataSize(IAmplitudeSettings.AscanDataSize.EightBits);
            rProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetScalingType(IAmplitudeSettings.ScalingType.Linear);
            rProbeBeamSet.GetDigitizingSettings().GetAmplitudeSettings().SetAscanRectification(IAmplitudeSettings.RectificationType.Full);
            // Add to the ultrasound configuration
            IConnector connectorPA = digitizerTechnology.GetConnectorCollection().GetConnector(0); // Adjust connector index if needed
            ultrasoundConfiguration.GetFiringBeamSetCollection().Add(lProbeBeamSet, connectorPA);
            ultrasoundConfiguration.GetFiringBeamSetCollection().Add(rProbeBeamSet, connectorPA);

            MessageBox.Show("Beamsets Lprobe and Rprobe successfully loaded from law files.");
            int lProbeBeamCount = (int)lProbeBeamSet.GetBeamCount();
            int rProbeBeamCount = (int)rProbeBeamSet.GetBeamCount();

            System.Diagnostics.Debug.WriteLine($"Lprobe beam count: {lProbeBeamCount}");
            System.Diagnostics.Debug.WriteLine($"Rprobe beam count: {rProbeBeamCount}");
        }

        public bool SetupAcquisition()
        {
            if (device == null)
            {
                return false;
            }

            try
            {
                acquisition = IAcquisition.CreateEx(device); // Correctly initialize acquisition using CreateEx method  
                acquisition.SetFiringTrigger(IAcquisition.FiringTrigger.Internal); // Set the firing trigger using the appropriate method  
                acquisition.SetRate(60);
                acquisition.ApplyConfiguration();
                acquisition.Start();

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Acquisition setup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
   
        

        public void InitiateAcquisition()
        {
            if (device == null)
            {
                return;
            }
            acquisition = IAcquisition.CreateEx(device);
        }

        public ICycleData CollectCycleData()
        {
            if (acquisition == null)
            {
                return null;
            }

            var result = acquisition.WaitForDataEx();
            if (result.status == IAcquisition.WaitForDataResultEx.Status.DataAvailable)
            {
                return result.cycleData;
            }

            return null;
        }

        public int[] CollectAscanData()
        {
            if (acquisition == null)
            {
                return null;
            }

            var result = acquisition.WaitForDataEx();
            if (result.status == IAcquisition.WaitForDataResultEx.Status.DataAvailable)
            {
                var cycleData = result.cycleData;
                var ascan = cycleData.GetAscanCollection().GetAscan(0);
                int[] ascanData = new int[ascan.GetSampleQuantity()];

                for (int i=0; i<ascan.GetSampleQuantity(); i++)
                {
                    ascanData[i] = (int)Marshal.ReadInt32(ascan.GetData(), i * 4);
                }
                return ascanData;
            }

            return null;
        }

        public void ConsumeData()
        {
            try
            {
                var dataResult = acquisition.WaitForDataEx();
                while (dataResult.status == IAcquisition.WaitForDataResultEx.Status.DataAvailable)
                {
                    using (var cycleData = dataResult.cycleData)
                        dataResult = acquisition.WaitForDataEx();
                }
                dataResult.Dispose();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }
        }
    }
}
