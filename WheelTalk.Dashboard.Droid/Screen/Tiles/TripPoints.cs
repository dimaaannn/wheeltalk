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
/// <b>Один на всё приложение.</b> Точки спрашивают двое: плитки-дистанции и счётчик поездки в центре
/// главного экрана (<see cref="Centre"/>). Хранилище у них одно, а каждый экземпляр держит свою копию
/// набора и пишет её целиком — два экземпляра над одной строкой затирали бы точки друг друга. Отсюда
/// и порядок: экземпляр выдаётся снаружи (у приложения — из состава служб, у стенда — полем
/// активности), а не заводится по месту.
/// </para>
/// <para>
/// Хранилища может не быть вовсе (<c>null</c>): тогда точки живут до конца работы. Так стенд
/// поднимается без файла и не падает.
/// </para>
/// </summary>
public sealed class TripPoints(ITripBaselineStore? store)
{
    /// <summary>
    /// Имя счётчика для дальности центра. Не имя плитки: имена плиток — восемь знаков шестнадцатичного
    /// (<c>MetricTile.NewId</c>), и слово из букв, которых в нём не бывает, с ними не столкнётся
    /// никогда. Точка у него своя на каждое колесо — как у плиток, по той же причине: одометр у
    /// каждого колеса свой.
    /// </summary>
    public const string Centre = "centre";

    private readonly TripBaselines _baselines = TripBaselines.Read(store?.Load());

    /// <summary>Путь этого счётчика на этом колесе. Точки нет — заводится и тут же сохраняется.</summary>
    public double Since(string wheel, string counter, double odometerKm)
    {
        int was = _baselines.Revision;
        double passed = _baselines.Since(wheel, counter, odometerKm);
        Keep(was);

        return passed;
    }

    /// <summary>Начать счёт заново — по слову хозяина: из меню плитки либо кнопкой шторки.</summary>
    public void Reset(string wheel, string counter, double odometerKm)
    {
        int was = _baselines.Revision;
        _baselines.Reset(wheel, counter, odometerKm);
        Keep(was);
    }

    /// <summary>
    /// Записать, если точки переменились. Без замыкания: <see cref="Since"/> зовут и с кадра панели
    /// (центр спрашивает свой счётчик на каждом), а лямбда там — мусор шестьдесят раз в секунду.
    /// </summary>
    private void Keep(int was)
    {
        if (_baselines.Revision != was) store?.Save(_baselines.Write());
    }
}
