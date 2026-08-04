namespace WheelTalk.Core.Contracts;

/// <summary>
/// Кто из семейств протоколов сообщает величину, а кто про неё молчит. Правило одно на всех, потому
/// что вывод из него один: <b>у молчащего колеса значения нет вовсе — это не ноль</b>. База пишет
/// сюда <c>NULL</c> (<c>RideStore.InsertTelemetry</c>), плитка рисует прочерк
/// (<c>MetricCatalogue</c>), и разойтись этим двоим нельзя: ровный ноль момента у InMotion —
/// такое же показание, как любое другое, и отличить его от молчания Veteran'а можно только по типу
/// колеса.
/// <para>
/// Проверка по типу, а не по значению, и живёт она здесь, а не в каждом месте, где нужна: новый
/// протокол, начавший сообщать момент, правится одной строкой, а не тремя, из которых третью
/// однажды не найдут.
/// </para>
/// </summary>
public static class WheelReports
{
    public static bool Veteran(TelemetrySnapshot snapshot) => snapshot.WheelType == WheelType.Veteran;

    /// <summary>Gotway и Begode — одно семейство и один протокол (<see cref="WheelType.GotWay"/>).</summary>
    public static bool Gotway(TelemetrySnapshot snapshot) => snapshot.WheelType == WheelType.GotWay;

    public static bool KingSong(TelemetrySnapshot snapshot) => snapshot.WheelType == WheelType.KingSong;

    /// <summary>Обе ветки InMotion: крен и температуру IMU сообщают и старая, и V2.</summary>
    public static bool InMotion(TelemetrySnapshot snapshot) =>
        snapshot.WheelType is WheelType.Inmotion or WheelType.InmotionV2;

    /// <summary>Только V2: момент, мощность мотора, температура CPU и лимит тока — её кадры.</summary>
    public static bool InMotionV2(TelemetrySnapshot snapshot) => snapshot.WheelType == WheelType.InmotionV2;
}
