using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets.Tape;

/// <summary>С какого края экрана стоит лента. От этого зависит, куда смотрит вся её разметка.</summary>
public enum TapeSide
{
    Left,
    Right,
}

/// <summary>
/// Пересчёт «значение → координата» и всё, что из него следует. Вынесен отдельно потому, что
/// части ленты рисуются независимо, а система координат у них общая: разъедься она хоть на пиксель,
/// стрелка перестанет указывать на деление, а окно — стоять на своём значении.
/// <para>
/// Здесь же живут размеры, которые нельзя задавать числом: ширина цветной полосы, кегли подписей.
/// Экраны отличаются не только плотностью, но и пропорциями, поэтому всё считается долей от того
/// места, которое ленте досталось, — но с потолком и полом, потому что читаемость измеряется в
/// угловых минутах, а не в долях экрана.
/// </para>
/// <para>
/// Портировано из <c>WheelTalk.Dashboard/Widgets/Tape/TapeGeometry.cs</c>: единственная правка —
/// <c>Microsoft.Maui.Graphics.RectF</c> заменён на <c>Android.Graphics.RectF</c> (конструктор той
/// принимает left/top/right/bottom, а не x/y/width/height, поэтому <see cref="LabelArea"/>
/// пересобран через границы, а не через ширину/высоту — результат тот же прямоугольник). Добавлен
/// параметр плотности экрана в конструктор — на Android координаты холста считаются в физических
/// пикселях, а не в dp, как у MAUI-канвы; без плотности экрана потолок и пол ширины полосы
/// (10…22, буквально dp у MAUI) на плотном экране превратились бы в нитку.
/// </para>
/// </summary>
public readonly struct TapeGeometry
{
    public TapeGeometry(RectF rect, TapeSide side, double value, double dpPerUnit, double windowAt, float density)
    {
        Rect = rect;
        Side = side;
        Value = value;
        DpPerUnit = dpPerUnit;
        WindowY = rect.Top + (float)(rect.Height() * windowAt);
        BandWidth = Math.Clamp(rect.Width() * 0.16f, 10f * density, 22f * density);
    }

    public RectF Rect { get; }
    public TapeSide Side { get; }

    /// <summary>Значение под окном — то, вокруг которого построена вся лента.</summary>
    public double Value { get; }

    /// <summary>
    /// Пикселей экрана на единицу величины. У MAUI-исходника это буквально dp на единицу; здесь
    /// вызывающий (<see cref="TapeDrawable"/>) обязан домножить его на плотность экрана до того,
    /// как передать сюда, — сам <see cref="TapeGeometry"/> об экране ничего не знает.
    /// </summary>
    public double DpPerUnit { get; }

    /// <summary>Высота, на которой стоит неподвижное окно значения.</summary>
    public float WindowY { get; }

    /// <summary>Ширина цветной полосы — доля ширины ленты, но не тоньше десяти точек.</summary>
    public float BandWidth { get; }

    /// <summary>Левый край цветной полосы. Полоса всегда у края экрана.</summary>
    public float BandLeft => Side == TapeSide.Right ? Rect.Right - BandWidth : Rect.Left;

    public float BandCenter => BandLeft + BandWidth / 2;

    /// <summary>Край полосы, от которого растут деления, — обращённый внутрь экрана.</summary>
    public float TickBase => Side == TapeSide.Right ? Rect.Right - BandWidth : Rect.Left + BandWidth;

    /// <summary>+1 или −1: куда от полосы идёт разметка.</summary>
    public int Inward => Side == TapeSide.Right ? -1 : 1;

    /// <summary>
    /// Часть ленты без цветной полосы — место под деления, подписи и окно значения. Окно занимает
    /// именно её, а не всю ширину: иначе оно накрывает полосу, а вместе с ней и всё, что на полосе
    /// нарисовано — стрелку просадки, риску следа, штриховку. Сигнал, спрятанный под цифрой,
    /// которая и так видна, — потерянный сигнал.
    /// </summary>
    public RectF LabelArea => Side == TapeSide.Right
        ? new RectF(Rect.Left, Rect.Top, Rect.Right - BandWidth, Rect.Bottom)
        : new RectF(Rect.Left + BandWidth, Rect.Top, Rect.Right, Rect.Bottom);

    public float ToY(double value) => WindowY - (float)((value - Value) * DpPerUnit);

    /// <summary>Значение у верхнего края видимой части ленты.</summary>
    public double TopValue => Value + (WindowY - Rect.Top) / DpPerUnit;

    /// <summary>Значение у нижнего края.</summary>
    public double BottomValue => Value - (Rect.Bottom - WindowY) / DpPerUnit;
}
