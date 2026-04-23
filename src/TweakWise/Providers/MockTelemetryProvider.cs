using System;
using System.Collections.Generic;
using TweakWise.Models;

namespace TweakWise.Providers
{
    public sealed class MockTelemetryProvider : ITelemetryProvider
    {
        private readonly Random _random = new Random(240424);

        private int _cpuUsage = 34;
        private int _gpuUsage = 22;
        private int _ramUsage = 58;
        private int _diskUsage = 16;

        private int _cpuTemp = 67;
        private int _gpuTemp = 61;
        private int _chipsetTemp = 49;
        private int _vrmTemp = 57;

        private int _batteryHealth = 91;
        private int _batteryCharge = 78;
        private int _ssdHealth = 95;
        private int _ssdWear = 21;

        private int _fanCpu = 1180;
        private int _fanCase = 940;
        private int _fanGpu = 1020;

        private int _tick;

        public TimeSpan RefreshInterval => TimeSpan.FromSeconds(6);

        public TelemetrySnapshot GetSnapshot()
        {
            _tick++;

            Nudge(ref _cpuUsage, 18, 72, 8);
            Nudge(ref _gpuUsage, 10, 64, 7);
            Nudge(ref _ramUsage, 46, 82, 4);
            Nudge(ref _diskUsage, 4, 58, 9);

            Nudge(ref _cpuTemp, 54, 88, 3);
            Nudge(ref _gpuTemp, 50, 83, 3);
            Nudge(ref _chipsetTemp, 40, 63, 2);
            Nudge(ref _vrmTemp, 45, 74, 2);

            Nudge(ref _batteryCharge, 61, 87, 2);
            Nudge(ref _fanCpu, 900, 1820, 120);
            Nudge(ref _fanCase, 700, 1500, 90);
            Nudge(ref _fanGpu, 760, 1680, 110);

            string activeProfileId = GetActiveProfileId();
            string activeProfileTitle = activeProfileId switch
            {
                "monitor-profile-quiet" => "Тихий",
                "monitor-profile-performance" => "Производительность",
                "monitor-profile-maximum" => "Максимум",
                _ => "Баланс"
            };

            var overallStatus = GetOverallStatus();
            string overallTitle = overallStatus switch
            {
                TelemetryStatus.Critical => "Нужен быстрый разбор нагрузки",
                TelemetryStatus.Attention => "Есть точки внимания",
                _ => "Система ведёт себя стабильно"
            };

            string overallDetail = overallStatus switch
            {
                TelemetryStatus.Critical => "Одна из зон нагрелась или упёрлась в нагрузку выше комфортного диапазона. Лучше проверить профиль и охлаждение.",
                TelemetryStatus.Attention => "Есть повышенные температуры или неидеальный фон, но без признаков аварийного состояния.",
                _ => "Нагрузка и температуры укладываются в обычный ежедневный сценарий без резких перекосов."
            };

            return new TelemetrySnapshot
            {
                CapturedAt = DateTime.Now,
                OverallStateTitle = overallTitle,
                OverallStateDetail = overallDetail,
                OverallStatus = overallStatus,
                ActiveProfileId = activeProfileId,
                ActiveProfileTitle = activeProfileTitle,
                CoolingSummary = $"{_fanCpu} RPM CPU • {_fanCase} RPM корпус • {_fanGpu} RPM GPU",
                UsageMetrics = new List<TelemetryMetric>
                {
                    CreateUsageMetric("cpu", "CPU", $"{_cpuUsage}%", $"Boost до 4.4 ГГц • {GetUsageStatusText(_cpuUsage)}", _cpuUsage, GetUtilizationStatus(_cpuUsage)),
                    CreateUsageMetric("gpu", "GPU", $"{_gpuUsage}%", $"3D / compositor • {GetUsageStatusText(_gpuUsage)}", _gpuUsage, GetUtilizationStatus(_gpuUsage)),
                    CreateUsageMetric("ram", "RAM", $"{_ramUsage}%", $"18.6 из 32 ГБ • {GetRamStatusText(_ramUsage)}", _ramUsage, GetMemoryStatus(_ramUsage)),
                    CreateUsageMetric("disk", "Диск", $"{_diskUsage}%", $"Активность NVMe • {GetUsageStatusText(_diskUsage)}", _diskUsage, GetDiskStatus(_diskUsage))
                },
                TemperatureMetrics = new List<TelemetryMetric>
                {
                    CreateTemperatureMetric("temp-cpu", "CPU Package", _cpuTemp, "Основная температура процессора"),
                    CreateTemperatureMetric("temp-gpu", "GPU Core", _gpuTemp, "Нагрузка графического ядра"),
                    CreateTemperatureMetric("temp-chipset", "Чипсет", _chipsetTemp, "Плата и логика ввода-вывода"),
                    CreateTemperatureMetric("temp-vrm", "Питание платы", _vrmTemp, "Зона VRM и питания")
                },
                HealthMetrics = new List<TelemetryMetric>
                {
                    new TelemetryMetric
                    {
                        Id = "battery-health",
                        Title = "Батарея",
                        PrimaryValue = $"Здоровье {_batteryHealth}%",
                        SecondaryValue = $"Заряд {_batteryCharge}% • 312 циклов",
                        UsagePercent = _batteryHealth,
                        Status = _batteryHealth >= 85 ? TelemetryStatus.Ok : _batteryHealth >= 72 ? TelemetryStatus.Attention : TelemetryStatus.Critical,
                        HistoryPercentages = BuildHistory(_batteryCharge, 8, 18),
                        HelpText = "Это mock-модель под будущий реальный источник состояния батареи."
                    },
                    new TelemetryMetric
                    {
                        Id = "ssd-health",
                        Title = "SSD",
                        PrimaryValue = $"Здоровье {_ssdHealth}%",
                        SecondaryValue = $"Износ {_ssdWear}% • 14.8 ТБ записано",
                        UsagePercent = _ssdHealth,
                        Status = _ssdHealth >= 90 ? TelemetryStatus.Ok : _ssdHealth >= 78 ? TelemetryStatus.Attention : TelemetryStatus.Critical,
                        HistoryPercentages = BuildHistory(_ssdHealth, 8, 4),
                        HelpText = "Архитектурная заготовка под SMART/health-источник."
                    }
                },
                FanMetrics = new List<TelemetryMetric>
                {
                    CreateFanMetric("fan-cpu", "CPU fan", _fanCpu, "Основной вентилятор процессора"),
                    CreateFanMetric("fan-case", "Корпус", _fanCase, "Проток воздуха по корпусу"),
                    CreateFanMetric("fan-gpu", "GPU fan", _fanGpu, "Охлаждение графического адаптера")
                }
            };
        }

        private string GetActiveProfileId()
        {
            if (_cpuTemp >= 82 || _gpuTemp >= 80)
                return "monitor-profile-maximum";

            if (_cpuUsage >= 62 || _gpuUsage >= 58)
                return "monitor-profile-performance";

            if (_batteryCharge <= 66)
                return "monitor-profile-quiet";

            return "monitor-profile-balance";
        }

        private TelemetryStatus GetOverallStatus()
        {
            if (_cpuTemp >= 84 || _gpuTemp >= 82 || _ssdHealth <= 72)
                return TelemetryStatus.Critical;

            if (_cpuTemp >= 76 || _gpuTemp >= 74 || _batteryHealth <= 80 || _ramUsage >= 76)
                return TelemetryStatus.Attention;

            return TelemetryStatus.Ok;
        }

        private TelemetryMetric CreateUsageMetric(
            string id,
            string title,
            string primaryValue,
            string secondaryValue,
            int usagePercent,
            TelemetryStatus status)
        {
            return new TelemetryMetric
            {
                Id = id,
                Title = title,
                PrimaryValue = primaryValue,
                SecondaryValue = secondaryValue,
                UsagePercent = usagePercent,
                Status = status,
                HistoryPercentages = BuildHistory(usagePercent, 8, 14),
                HelpText = "Mock usage-метрика под будущий live refresh."
            };
        }

        private TelemetryMetric CreateTemperatureMetric(string id, string title, int value, string secondaryValue)
        {
            return new TelemetryMetric
            {
                Id = id,
                Title = title,
                PrimaryValue = $"{value}°C",
                SecondaryValue = secondaryValue,
                UsagePercent = Math.Min(100, value),
                Status = GetTemperatureStatus(value),
                HistoryPercentages = BuildHistory(value, 8, 5),
                HelpText = "Mock sensor-метрика под будущий источник датчиков."
            };
        }

        private TelemetryMetric CreateFanMetric(string id, string title, int rpm, string secondaryValue)
        {
            int usagePercent = Math.Min(100, (int)Math.Round((rpm / 2200d) * 100));

            return new TelemetryMetric
            {
                Id = id,
                Title = title,
                PrimaryValue = $"{rpm} RPM",
                SecondaryValue = secondaryValue,
                UsagePercent = usagePercent,
                Status = rpm >= 1600 ? TelemetryStatus.Attention : TelemetryStatus.Ok,
                HistoryPercentages = BuildHistory(usagePercent, 8, 12),
                HelpText = "Mock fan speed под будущий источник оборотов."
            };
        }

        private List<int> BuildHistory(int currentValue, int points, int variance)
        {
            var values = new List<int>();

            for (int index = 0; index < points; index++)
            {
                int offset = _random.Next(-variance, variance + 1);
                values.Add(Math.Clamp(currentValue + offset, 2, 100));
            }

            return values;
        }

        private static TelemetryStatus GetTemperatureStatus(int temperature)
        {
            if (temperature >= 82)
                return TelemetryStatus.Critical;

            return temperature >= 74 ? TelemetryStatus.Attention : TelemetryStatus.Ok;
        }

        private static TelemetryStatus GetUtilizationStatus(int usagePercent)
        {
            if (usagePercent >= 88)
                return TelemetryStatus.Critical;

            return usagePercent >= 72 ? TelemetryStatus.Attention : TelemetryStatus.Ok;
        }

        private static TelemetryStatus GetMemoryStatus(int usagePercent)
        {
            if (usagePercent >= 90)
                return TelemetryStatus.Critical;

            return usagePercent >= 78 ? TelemetryStatus.Attention : TelemetryStatus.Ok;
        }

        private static TelemetryStatus GetDiskStatus(int usagePercent)
        {
            if (usagePercent >= 92)
                return TelemetryStatus.Critical;

            return usagePercent >= 70 ? TelemetryStatus.Attention : TelemetryStatus.Ok;
        }

        private static string GetUsageStatusText(int usagePercent)
        {
            return usagePercent >= 70 ? "плотная нагрузка" : usagePercent >= 40 ? "рабочая нагрузка" : "спокойный режим";
        }

        private static string GetRamStatusText(int usagePercent)
        {
            return usagePercent >= 74 ? "запас уменьшается" : "комфортный запас";
        }

        private void Nudge(ref int value, int min, int max, int step)
        {
            int delta = _random.Next(-step, step + 1);
            value = Math.Clamp(value + delta, min, max);
        }
    }
}
