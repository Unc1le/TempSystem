using System;
using System.Collections.Generic;
using System.Text;
using TempSystem.Models;

namespace TempSystem.Services
{
    internal class TemperatureController
    {
        // Значения взяты из тепловой модели эмулятора.
        public const double HeatCompensation =
            (6.03 + 1.5) / (6.03 * 0.7);

        private const double Kp = 2.0;

        public double Calculate(SystemState state, double target)
        {
            double baseSetpoint = state.TAmbient +
                (target - state.TAmbient) * HeatCompensation;

            double error = target - state.TSystem;
            double setpoint = baseSetpoint + Kp * error;

            return Math.Round(Math.Clamp(setpoint, 20.0, 450.0), 1);
        }

        public double GetMinimumTarget(double ambient)
        {
            // Нагреватель не умеет охлаждать и имеет минимальную уставку 20 °C.
            double minimumHeater = Math.Max(20.0, ambient);
            return ambient + (minimumHeater - ambient) / HeatCompensation;
        }
    }
}
