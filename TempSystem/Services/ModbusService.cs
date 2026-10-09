using NModbus;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using TempSystem.Models;

namespace TempSystem.Services
{
    internal class ModbusService
    {
        private TcpClient? client;
        private IModbusMaster? master;

        public async Task Connect(string ip, int port)
        {
            Disconnect();
            client = new TcpClient();

            try
            {
                using var timeout = new CancellationTokenSource(2000);
                await client.ConnectAsync(ip, port, timeout.Token);

                var factory = new ModbusFactory();
                master = factory.CreateMaster(client);
                master.Transport.ReadTimeout = 1000;
                master.Transport.WriteTimeout = 1000;
                master.Transport.Retries = 0;
                master.Transport.SlaveBusyUsesRetryCount = true;
            }
            catch { Disconnect(); throw; }
        }

        private IModbusMaster GetMaster()
        { return master ?? throw new InvalidOperationException("Нет подключения."); }
        
        public SystemState ReadState()
        {
            var master = GetMaster();
            ushort[] system = master.ReadInputRegisters(1, 0, 2);
            ushort[] heater = master.ReadInputRegisters(2, 0, 2);
            ushort[] ambient = master.ReadInputRegisters(3, 0, 2);
            ushort[] pressure = master.ReadInputRegisters(4, 0, 2);
            ushort[] pid = master.ReadHoldingRegisters(5, 0, 2);

            return new SystemState
            {
                TSystem = unchecked((short)system[0]) / 10.0,
                THeater = unchecked((short)heater[0]) / 10.0,
                TAmbient = unchecked((short)ambient[0]) / 10.0,
                Pressure = pressure[0] / 10.0,

                SystemSensorStatus = system[1],
                HeaterSensorStatus = heater[1],
                AmbientSensorStatus = ambient[1],
                PressureSensorStatus = pressure[1],

                HeaterSetpoint = unchecked((short)pid[0]) / 10.0,
                HeaterEnabled = pid[1] == 1
            };
        }

        private ushort EncodeSetpoint(double temperature)
        {
            if (!double.IsFinite(temperature) ||
                temperature < 20.0 || temperature > 450.0)
            {
                throw new ArgumentException("Уставка должна быть от 20 до 450 °C.");
            }

            return (ushort)Math.Round(temperature * 10.0);
        }

        public void InitializeHeater()
        {
            GetMaster().WriteMultipleRegisters(5, 0, new ushort[] { 500, 1 });
        }

        public void StartHeating(double setpoint)
        {
            GetMaster().WriteMultipleRegisters(
                5, 0, new ushort[] { EncodeSetpoint(setpoint), 1 });
        }

        public void SetSetpoint(double setpoint)
        {
            GetMaster().WriteSingleRegister(5, 0, EncodeSetpoint(setpoint));
        }

        public void SetHeaterEnabled(bool enabled)
        {
            GetMaster().WriteSingleRegister(5, 1, (ushort)(enabled ? 1 : 0));
        }

        public void Disconnect()
        {
            master?.Dispose();
            master = null;
            client?.Dispose();
            client = null;
        }
    }
}