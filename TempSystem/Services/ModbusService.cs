using NModbus;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using TempSystem.Models;

namespace TempSystem.Services
{
    internal class ModbusService
    {
        private TcpClient? client;
        private IModbusMaster? master;

        public event EventHandler<Exception>? ConnectionLost;

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

        private T Execute<T>(Func<IModbusMaster, T> action, T failureResult)
        {
            var activeMaster = master;
            if (activeMaster == null)
                return failureResult;

            try
            {
                return action(activeMaster);
            }
            catch (Exception ex) when (ex is IOException or SocketException or
                TimeoutException or ObjectDisposedException)
            {
                Disconnect();
                ConnectionLost?.Invoke(this, ex);
                return failureResult;
            }
        }

        private bool Execute(Action<IModbusMaster> action)
        {
            return Execute(activeMaster =>
            {
                action(activeMaster);
                return true;
            }, false);
        }
        
        public SystemState? ReadState()
        {
            return Execute<SystemState?>(master =>
            {
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
            }, null);
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

        public bool InitializeHeater()
        {
            return Execute(master => master.WriteMultipleRegisters(5, 0, new ushort[] { 500, 1 }));
        }

        public bool StartHeating(double setpoint)
        {
            return Execute(master => master.WriteMultipleRegisters(
                5, 0, new ushort[] { EncodeSetpoint(setpoint), 1 }));
        }

        public bool SetSetpoint(double setpoint)
        {
            return Execute(master => master.WriteSingleRegister(5, 0, EncodeSetpoint(setpoint)));
        }

        public bool SetHeaterEnabled(bool enabled)
        {
            return Execute(master => master.WriteSingleRegister(5, 1, (ushort)(enabled ? 1 : 0)));
        }

        public void Disconnect()
        {
            var disconnectedMaster = master;
            var disconnectedClient = client;
            master = null;
            client = null;

            DisposeSafely(disconnectedMaster);
            DisposeSafely(disconnectedClient);
        }

        private static void DisposeSafely(IDisposable? resource)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Ошибка освобождения TCP-соединения: " + ex);
            }
        }
    }
}
