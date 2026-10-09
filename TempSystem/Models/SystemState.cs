using System;
using System.Collections.Generic;
using System.Text;

namespace TempSystem.Models
{
    internal class SystemState
    {
        public double TSystem { get; set; }
        public double THeater { get; set; }
        public double TAmbient { get; set; }
        public double Pressure { get; set; }

        public ushort SystemSensorStatus { get; set; }
        public ushort HeaterSensorStatus { get; set; }
        public ushort AmbientSensorStatus { get; set; }
        public ushort PressureSensorStatus { get; set; }

        public double HeaterSetpoint { get; set; }
        public bool HeaterEnabled { get; set; }

        public bool HasSensorError => SystemSensorStatus != 0 || HeaterSensorStatus != 0 || AmbientSensorStatus != 0 || PressureSensorStatus != 0;
    }
}