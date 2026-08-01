using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;
using WheelTalk.Dashboard.Droid;

namespace WheelTalk.Droid.Main;

/// <summary>
/// Один кадр панели: снимок телеметрии плюс то, что про поездку знает <see cref="RideTrace"/>.
/// Отдельный шов между ядром и рисованием — чтобы <see cref="RideTrace"/> не знал про панель (он
/// про поездку, а не про экран), а панель не знала, откуда взялись её величины: стенд подаёт в неё
/// записанный файл и придуманный сценарий, приложение — живое колесо.
/// <para>
/// Портировано из <c>WheelTalk.App/Dashboard/DashboardFrame.cs</c> без изменений логики — только
/// пространство имён и ссылка на <c>WheelTalk.Dashboard</c> → <c>WheelTalk.Dashboard.Droid</c>.
/// </para>
/// </summary>
internal static class DashboardFrame
{
    public static DashboardReading From(TelemetrySnapshot snapshot, RideTrace trace, double alertIntensity) =>
        DashboardReading.From(snapshot, trace.PwmRate, alertIntensity, trace.RecentPwmPeak)
            with
            {
                // Сглаженный ШИМ вместо сырого: на нём же посчитана производная, и лента со
                // стрелкой должны показывать одно и то же значение, а не два соседних.
                Pwm = trace.Pwm,
                SpeedRate = trace.SpeedRate,
                MinVoltageV = trace.MinVoltageV,
                MaxVoltageV = trace.MaxVoltageV,
                NoLoadVoltageV = trace.NoLoadVoltageV,
                MaxSagV = trace.MaxSagV,
                MaxTemperatureC = trace.MaxTemperatureC,
            };
}
