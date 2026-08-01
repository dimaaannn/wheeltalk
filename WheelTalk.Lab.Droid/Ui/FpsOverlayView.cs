using Android.Content;
using Android.Graphics;
using Android.Views;

namespace WheelTalk.Lab.Droid.Ui;

/// <summary>
/// Счётчик кадров. Считает **свои собственные вызовы <c>OnDraw</c>**, а не тики какого-нибудь
/// таймера: таймер говорит, сколько раз мы попросили перерисовать, а нужно знать, сколько раз
/// перерисовка действительно случилась. Разница между этими числами и есть то, что ищут.
/// <para>
/// Три числа те же, что у MAUI-стенда (<c>WheelTalk.Lab/Pages/FpsOverlayDrawable.cs</c>): кадров за
/// последнюю секунду, самый долгий промежуток между кадрами и стоимость самой отрисовки панели.
/// Среднего мало — панель, роняющая раз в секунду один кадр на сотню миллисекунд, по среднему
/// выглядит здоровой, а глазом видна как рывок.
/// </para>
/// <para>
/// Стоимость отрисовки здесь не измеряется снаружи, как в MAUI-версии (там стенд обкладывал
/// секундомером свои <c>Show</c> и <c>Invalidate</c>): панель рисует себя сама по vsync, и снаружи
/// её кадр не виден вовсе. Число приходит из <c>DashboardView.LastDrawMs</c> — оно измерено внутри
/// того самого <c>OnDraw</c>, который и стоит дорого.
/// </para>
/// </summary>
public sealed class FpsOverlayView(Context context) : View(context)
{
    private readonly Paint _text = new() { AntiAlias = true, Color = Color.ParseColor("#009E73") };
    private readonly float _density = context.Resources?.DisplayMetrics?.Density ?? 1;

    private long _windowStart;
    private long _lastFrame;
    private int _frames;
    private double _worstMs;

    public int Fps { get; private set; }
    public double WorstFrameMs { get; private set; }

    /// <summary>Сколько заняла последняя отрисовка панели. Ставит стенд из <c>DashboardView.LastDrawMs</c>.</summary>
    public double PanelDrawMs { get; set; }

    protected override void OnDraw(Canvas canvas)
    {
        long now = Java.Lang.JavaSystem.NanoTime();

        if (_lastFrame != 0)
        {
            _worstMs = Math.Max(_worstMs, Elapsed(_lastFrame, now));
        }
        _lastFrame = now;
        _frames++;

        if (_windowStart == 0)
        {
            _windowStart = now;
        }
        else if (Elapsed(_windowStart, now) >= 1000)
        {
            Fps = _frames;
            WorstFrameMs = _worstMs;
            _windowStart = now;
            _frames = 0;
            _worstMs = 0;
        }

        _text.TextSize = 13 * _density;
        var metrics = _text.GetFontMetrics()!;
        canvas.DrawText(
            $"нативно · {Fps} fps · кадр {WorstFrameMs:F1} мс худший · отрисовка {PanelDrawMs:F2} мс",
            8 * _density, Height / 2f - (metrics.Ascent + metrics.Descent) / 2, _text);

        PostInvalidateOnAnimation();
    }

    private static double Elapsed(long from, long to) => (to - from) / 1_000_000.0;
}
