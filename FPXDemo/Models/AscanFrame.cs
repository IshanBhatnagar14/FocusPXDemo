using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FPXDemo.Models
{
    public class AscanFrame
    {
        public int BeamIndex { get; set; }             // Identifies which beam this A-scan belongs to
        public double Gain { get; set; }               // Gain applied to this A-scan
        public int Amplitude { get; set; }             // Peak or maximum amplitude, optional
        public int[] Signal { get; set; }              // Y-axis: Amplitude values over time
        public double[] TimeAxis { get; set; }         // X-axis: Time values (calculated from GetTimeDataRange)
    }
}
