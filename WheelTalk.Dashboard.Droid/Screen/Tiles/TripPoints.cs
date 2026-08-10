using WheelTalk.Core.Tiles;

namespace WheelTalk.Dashboard.Droid.Screen.Tiles;

/// <summary>
/// Точки отсчёта дистанций на стороне экрана: считает их ядро (<see cref="TripBaselines"/>), а
/// здесь — чтение из хранилища при первом обращении и запись после каждой перемены.
/// <para>
/// <b>Записывается по счётчику перемен, а не по расписанию.</b> Показ зовут на каждом снимке, и
/// писать хранилище кадром было бы разорительно; но и молчать нельзя — заведённая точка, не
/// доехавшая до диска, обнулила бы дистанцию при первом же перезапуске.
/// </para>
/// <para>
/// Хранилища может не быть вовсе (<c>null</c>): тогда точки живут до конца работы экрана. Так
/// стенд поднимается без файла и не падает.
/// </para>
/// </summary>
internal sealed class TripPoints(ITripBaselineStore? store)
{
    private readonly TripBaselines _baselines = TripBaselines.Read(store?.Load());

    /// <summary>Путь этой плитки на этом колесе. Точки нет — заводится и тут же сохраняется.</summary>
    public double Since(string wheel, string tile, double odometerKm) =>
        Keeping(() => _baselines.Since(wheel, tile, odometerKm));

    /// <summary>Начать счёт заново — по слову хозяина из меню плитки.</summary>
    public void Reset(string wheel, string tile, double odometerKm) =>
        Keeping(() =>
        {
            _baselines.Reset(wheel, tile, odometerKm);
            return 0d;
        });

    private double Keeping(Func<double> change)
    {
        int was = _baselines.Revision;
        double value = change();

        if (_baselines.Revision != was) store?.Save(_baselines.Write());

        return value;
    }
}
