namespace WheelTalk.Droid.Configuration;

/// <summary>
/// Как приложение ведёт себя вокруг разбора поломок. Отдельно от <see cref="PowerOptions"/> и
/// <see cref="ScreenOptions"/> нарочно: та экономия заряда и этот экран — про поездку, а здесь —
/// про то, что происходит после того, как что-то уже сломалось.
/// </summary>
public sealed class DiagnosticsOptions
{
    public const string SectionName = "Diagnostics";

    /// <summary>
    /// Предложить отправить журнал на старте, если прошлый запуск не завершился штатно
    /// (<c>CrashReport.PreviousRunCrashed</c>). Заводское — включено: тот, кому это мешает, выключит
    /// сам, а тот, у кого приложение падает молча, иначе никогда не узнает, что есть чем помочь
    /// разбору. Галочка «Больше не предлагать» в самом диалоге выключает этот же флаг — кнопка
    /// «Передать» здесь ни при чём, она остаётся доступной всегда.
    /// </summary>
    public bool PromptShareAfterCrash { get; set; } = true;
}
