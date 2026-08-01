namespace WheelTalk.Core.Detection;

/// <summary>
/// Что мы умеем с опознанным семейством. Отдельно от <see cref="WheelDetector"/> нарочно:
/// опознание — про железо и не меняется, а список поддержанного — про нас и растёт.
/// </summary>
public static class WheelFamilies
{
    /// <summary>
    /// Умеем ли мы говорить с этим семейством. Сегодня — Begode, Veteran и KingSong; отпечатки
    /// InMotion и Ninebot лежат в таблице ради честного ответа «это InMotion, мы его пока не
    /// умеем» вместо «непонятное устройство».
    /// <para>
    /// Какой именно из двух протоколов внутри семейства Gotway, здесь не решается и решаться не
    /// может: профиль `FFE0`/`FFE1` у Begode и Veteran общий, и различает их только заголовок
    /// первого пришедшего кадра — этим занимается декодер (порт `GotwayVirtualAdapter`).
    /// </para>
    /// </summary>
    public static bool IsSupported(WheelFamily family) =>
        family is WheelFamily.Gotway or WheelFamily.KingSong or WheelFamily.InMotion or WheelFamily.InMotionV2;

    /// <summary>Имя семейства для человека.</summary>
    public static string DisplayName(WheelFamily family) => family switch
    {
        WheelFamily.Gotway => "Begode / Veteran",
        WheelFamily.KingSong => "KingSong",
        WheelFamily.InMotion => "InMotion",
        WheelFamily.InMotionV2 => "InMotion V2",
        WheelFamily.Ninebot => "Ninebot",
        WheelFamily.NinebotZ => "Ninebot Z",
        _ => family.ToString(),
    };
}
