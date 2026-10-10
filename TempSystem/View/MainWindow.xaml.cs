using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TempSystem.Chart;
using TempSystem.Models;
using TempSystem.Services;

namespace TempSystem
{
    public partial class MainWindow : Window
    {
        private readonly ModbusService _modbus = new();
        private readonly TemperatureController _controller = new();
        private readonly DispatcherTimer _timer = new();
        private readonly ChartViewModel _chart;

        private bool _connected;
        private bool _running;
        private bool _busy;
        private bool _closing;
        private bool _connectionLostHandled;
        private double _target;

        public MainWindow()
        {
            InitializeComponent();
            _chart = (ChartViewModel)DataContext;
            _modbus.ConnectionLost += Modbus_ConnectionLost;
            Closing += Window_Closing;
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += Timer_Tick;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool ready = !_busy && !_closing;
            IpAddressTextBox.IsEnabled = ready && !_connected;
            PortTextBox.IsEnabled = ready && !_connected;
            ConnectButton.IsEnabled = ready;
            ConnectButton.Content = _connected ? "Отключить" : "Подключить";
            InitializeButton.IsEnabled = ready && _connected;
            StartButton.IsEnabled = ready && _connected && !_running;
            StopButton.IsEnabled = ready && _connected;
            TargetTemperatureTextBox.IsEnabled = !_closing && !_running;
            ControlStateText.Text = _running ? "Запущено" : "Остановлено";
        }

        private void ShowState(SystemState? state)
        {
            SystemTemperatureValue.Text = state?.TSystem.ToString("F1") ?? "—";
            HeaterTemperatureValue.Text = state?.THeater.ToString("F1") ?? "—";
            AmbientTemperatureValue.Text = state?.TAmbient.ToString("F1") ?? "—";
            PressureValue.Text = state?.Pressure.ToString("F1") ?? "—";
            HeaterSetpointValue.Text = state?.HeaterSetpoint.ToString("F1") ?? "—";
            SystemStatusText.Text = SensorStatus(state?.SystemSensorStatus);
            HeaterStatusText.Text = SensorStatus(state?.HeaterSensorStatus);
            AmbientStatusText.Text = SensorStatus(state?.AmbientSensorStatus);
            PressureStatusText.Text = SensorStatus(state?.PressureSensorStatus);
        }

        private string SensorStatus(ushort? status)
        {
            if (status == null)
                return "Нет связи";
            return status == 0 ? "ОК" : "Ошибка";
        }

        private void Disconnect()
        {
            _timer.Stop();
            _running = false;
            _connected = false;
            _modbus.Disconnect();
            ShowState(null);
            ConnectionStateText.Text = "Нет связи";
            ConnectionStateText.Foreground = Brushes.Maroon;
        }

        private void Modbus_ConnectionLost(object? sender, Exception error)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Modbus_ConnectionLost(sender, error));
                return;
            }

            if (_connectionLostHandled)
                return;

            _connectionLostHandled = true;
            Disconnect();
            ConnectionStateText.Text = "Соединение потеряно";
            StatusText.Text = "TCP-соединение потеряно. Автоматическое управление остановлено. " +
                "Подключитесь повторно. " + error.Message;
            UpdateButtons();

            if (!_closing)
                MessageBox.Show(this, StatusText.Text, "Соединение потеряно",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private async Task RunAsync(Func<Task> action)
        {
            if (_busy || _closing)
                return;

            _busy = true;
            _connectionLostHandled = false;
            UpdateButtons();
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                // Транспорт уже сообщил о разрыве через ConnectionLost.
                if (!_connectionLostHandled)
                {
                    Disconnect();
                    ConnectionStateText.Text = "Ошибка / нет связи";
                    StatusText.Text = "Ошибка связи. Управление остановлено. " + ex.Message;
                    if (!_closing)
                        MessageBox.Show(this, StatusText.Text, "Ошибка связи",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                _busy = false;
                UpdateButtons();
            }
        }

        private async Task<SystemState?> ReadStateAsync()
        {
            var state = await Task.Run(_modbus.ReadState);
            if (state == null || _closing)
                return null;

            ShowState(state);
            _chart.AddMeasurement(
                state.SystemSensorStatus == 0 ? state.TSystem : null,
                state.HeaterSensorStatus == 0 ? state.THeater : null);
            if (!state.HasSensorError && state.TSystem < 160 && state.THeater < 470)
                return state;

            _running = false;
            if (state.HeaterEnabled &&
                !await Task.Run(() => _modbus.SetHeaterEnabled(false)))
                return null;

            StatusText.Text = state.HasSensorError
                ? "Ошибка датчика. Управление остановлено."
                : "Аварийная температура. Управление остановлено.";
            return null;
        }

        private async Task InitializeHeaterAsync()
        {
            _running = false;
            if (await ReadStateAsync() == null || _closing)
                return;

            if (!await Task.Run(_modbus.InitializeHeater) || _closing)
                return;
            HeaterSetpointValue.Text = 50.0.ToString("F1");
            StatusText.Text = "ПИД включён. Начальная уставка: 50 °C.";
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(async () =>
            {
                if (_connected)
                {
                    if (!await Task.Run(() => _modbus.SetHeaterEnabled(false)) || _closing)
                        return;
                    Disconnect();
                    ConnectionStateText.Foreground = Brushes.Maroon;
                    StatusText.Text = "Отключено.";
                    return;
                }

                string ip = IpAddressTextBox.Text.Trim();

                if (!IPAddress.TryParse(ip, out _) || !int.TryParse(PortTextBox.Text, out int port) || port < 1 || port > 65535)
                {
                    MessageBox.Show("Введите IP-адрес и порт от 1 до 65535.");
                    return;
                }


                ConnectionStateText.Text = "Подключение...";
                ConnectionStateText.Foreground = Brushes.Olive;
                await _modbus.Connect(ip, port);
                _connected = true;
                if (_closing) return;

                _chart.Clear();
                ConnectionStateText.Text = "Подключено";
                ConnectionStateText.Foreground = Brushes.Green;
                await InitializeHeaterAsync();
                if (!_closing && _connected) _timer.Start();
            });
        }

        private async void InitializeButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(InitializeHeaterAsync);
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            string text = TargetTemperatureTextBox.Text.Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double target) || !double.IsFinite(target) || target < 15 || target > 150)
            {
                MessageBox.Show("Введите температуру от 15 до 150 °C.");
                return;
            }

            await RunAsync(async () =>
            {
                var state = await ReadStateAsync();
                if (state == null || _closing)
                    return;

                double minimum = _controller.GetMinimumTarget(state.TAmbient);
                if (target < minimum - 0.2)
                {
                    MessageBox.Show($"Минимальная достижимая цель: {minimum:F1} °C. " +
                        "В эмуляторе нет активного охлаждения.");
                    return;
                }

                _target = target;
                double setpoint = _controller.Calculate(state, _target);
                if (!await Task.Run(() => _modbus.StartHeating(setpoint)) || _closing)
                    return;

                _running = true;
                HeaterSetpointValue.Text = setpoint.ToString("F1");
                StatusText.Text = "Автоматическое управление запущено.";
            });
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAsync(async () =>
            {
                _running = false;
                if (!await Task.Run(() => _modbus.SetHeaterEnabled(false)) || _closing)
                    return;
                StatusText.Text = "Управление остановлено. ПИД выключен.";
            });
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_connected)
                return;

            await RunAsync(async () =>
            {
                var state = await ReadStateAsync();
                if (state == null || _closing)
                    return;

                if (_running && !state.HeaterEnabled)
                {
                    _running = false;
                    StatusText.Text = "ПИД выключен. Управление остановлено.";
                    return;
                }

                if (_running)
                {
                    double setpoint = _controller.Calculate(state, _target);
                    if (!await Task.Run(() => _modbus.SetSetpoint(setpoint)) || _closing)
                        return;
                    HeaterSetpointValue.Text = setpoint.ToString("F1");
                }

                StatusText.Text = "Опрос: 500 мс. Обновлено: " +
                    DateTime.Now.ToString("HH:mm:ss");
            });
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            e.Cancel = true;
            if (_closing)
                return;

            _closing = true;
            _running = false;
            _timer.Stop();
            UpdateButtons();
            while (_busy)
                await Task.Delay(50);

            try
            {
                if (_connected && !await Task.Run(() => _modbus.SetHeaterEnabled(false)))
                    MessageBox.Show("Не удалось выключить ПИД: соединение потеряно.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось выключить ПИД: " + ex.Message);
            }
            finally
            {
                Disconnect();
                _modbus.ConnectionLost -= Modbus_ConnectionLost;
                // Повторный Close должен выполняться после выхода из исходного Closing.
                Closing -= Window_Closing;
                _ = Dispatcher.BeginInvoke(new Action(Close));
            }
        }
    }
}
