using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using System.Collections.ObjectModel;

namespace TempSystem.Chart
{
    public class ChartViewModel
    {
        private const int MaxPoints = 1200;
        private readonly ObservableCollection<DateTimePoint> _systemValues = new();
        private readonly ObservableCollection<DateTimePoint> _heaterValues = new();

        public ISeries[] Series { get; }

        public Axis[] XAxes { get; } =
        {
            new DateTimeAxis(TimeSpan.FromMilliseconds(500), date => date.ToString("HH:mm:ss"))
            {
                Name = "Время",
                MinStep = TimeSpan.FromSeconds(1).Ticks
            }
        };

        public Axis[] YAxes { get; } =
        {
            new Axis
            {
                Name = "Температура, °C",
                Labeler = value => value.ToString("F1")
            }
        };

        public ChartViewModel()
        {
            Series = new ISeries[]
            {
                new LineSeries<DateTimePoint>
                {
                    Name = "T_system",
                    Values = _systemValues,
                    Fill = null,
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    EnableNullSplitting = true
                },
                new LineSeries<DateTimePoint>
                {
                    Name = "T_heater",
                    Values = _heaterValues,
                    Fill = null,
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    EnableNullSplitting = true
                }
            };
        }

        public void AddMeasurement(double? tSystem, double? tHeater)
        {
            DateTime timestamp = DateTime.Now;
            _systemValues.Add(new DateTimePoint(timestamp, tSystem));
            _heaterValues.Add(new DateTimePoint(timestamp, tHeater));

            if (_systemValues.Count > MaxPoints)
            {
                _systemValues.RemoveAt(0);
                _heaterValues.RemoveAt(0);
            }
        }

        public void Clear()
        {
            _systemValues.Clear();
            _heaterValues.Clear();
        }
    }
}
