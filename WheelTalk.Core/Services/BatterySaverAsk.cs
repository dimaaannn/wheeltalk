namespace WheelTalk.Core.Services;

/// <summary>Итог решения: спрашивать ли сейчас, и что после этого хранить в счётчике попыток.</summary>
public readonly record struct BatterySaverAskDecision(bool ShouldAsk, int NextAskCount);

/// <summary>
/// Сколько раз просить исключить приложение из экономии заряда при старте (bugfix 3 §3.1).
/// Оригинал и порт спрашивают на каждом запуске, пока система не считает исключение выданным — на
/// части прошивок (вендорские списки вместо системного, Doze после перезагрузки) это никогда не
/// проходит, и приложение выпрашивает разрешение до посинения. Решение владельца 09.08.2026:
/// спросить три раза при старте, дальше молчать.
/// <para>
/// Чистая функция от тумблера, ответа ОС и счётчика прошлых попыток — проверяется тестами, а не
/// подсчётом диалогов на телефоне. Показ решает вызывающий (может не открыться на прошивке без
/// экрана) — но считается именно попытка, а не успех: <see cref="BatterySaverAskDecision.NextAskCount"/>
/// растёт независимо от того, откроется ли системный экран.
/// </para>
/// </summary>
public static class BatterySaverAsk
{
    /// <summary>Порог, после которого приложение молчит. Не настройка — крутить его некому и незачем.</summary>
    public const int MaxAsks = 3;

    /// <param name="warnEnabled">Тумблер «предупреждать об экономии заряда».</param>
    /// <param name="isIgnoringOptimizations">Исключение уже выдано системой.</param>
    /// <param name="asksSoFar">Сколько раз уже спрашивали с последнего включения тумблера.</param>
    public static BatterySaverAskDecision Decide(bool warnEnabled, bool isIgnoringOptimizations, int asksSoFar)
    {
        // Выключенный тумблер — чистый лист: включат заново, и приложение спросит снова три раза,
        // а не молчит до конца времён из-за счётчика, накопленного до выключения. Настройки такой
        // ("спросить ещё раз") в каталоге нет, и без этого сброса тумблер стал бы мёртвым.
        if (!warnEnabled) return new BatterySaverAskDecision(false, 0);

        // Исключение выдано — счётчик не трогаем: потратить попытку на случай, когда просить не
        // пришлось, значит потерять её зря.
        if (isIgnoringOptimizations) return new BatterySaverAskDecision(false, asksSoFar);

        if (asksSoFar >= MaxAsks) return new BatterySaverAskDecision(false, asksSoFar);

        return new BatterySaverAskDecision(true, asksSoFar + 1);
    }
}
