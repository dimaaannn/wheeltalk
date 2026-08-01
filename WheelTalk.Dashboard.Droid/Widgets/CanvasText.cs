using Android.Graphics;

namespace WheelTalk.Dashboard.Droid.Widgets;

/// <summary>Горизонтальное выравнивание — то же деление, что у <c>Microsoft.Maui.Graphics.HorizontalAlignment</c>.</summary>
public enum HAlign { Left, Center, Right }

/// <summary>Вертикальное выравнивание — аналог <c>Microsoft.Maui.Graphics.VerticalAlignment</c>.</summary>
public enum VAlign { Top, Center, Bottom }

/// <summary>
/// У <see cref="Canvas"/> нет аналога <c>ICanvas.DrawString(text, x, y, width, height, hAlign, vAlign)</c>:
/// Android рисует текст по базовой линии одной точки, а не в прямоугольнике. Этот хелпер
/// восстанавливает ту же геометрию через <c>Paint.FontMetrics</c>, чтобы каждая перенесённая
/// часть панели читалась как построчная копия MAUI-исходника (тот же вызов, тот же порядок
/// аргументов), а не как самостоятельный расчёт базовой линии в каждом файле по отдельности.
/// </summary>
public static class CanvasText
{
    public static void DrawString(this Canvas canvas, Paint paint, string text,
        float boxLeft, float boxTop, float boxWidth, float boxHeight, HAlign hAlign, VAlign vAlign)
    {
        paint.TextAlign = hAlign switch
        {
            HAlign.Left => Paint.Align.Left,
            HAlign.Right => Paint.Align.Right,
            _ => Paint.Align.Center,
        };

        float x = hAlign switch
        {
            HAlign.Left => boxLeft,
            HAlign.Right => boxLeft + boxWidth,
            _ => boxLeft + boxWidth / 2,
        };

        // Ascent()/Descent() вместо GetFontMetrics(): те же два числа, но примитивами. GetFontMetrics
        // создаёт Java-объект, а каждый Java-объект, рождённый из управляемого кода, стоит перехода
        // через JNI и регистрации в GC-мосту. Подписей на панели около сорока за кадр, и на телефоне
        // это измеримая часть кадра (замер 29.07.2026: наша отрисовка 35 мс против 8,5 мс у бенча,
        // у которого такого вызова нет вовсе).
        float ascent = paint.Ascent();
        float descent = paint.Descent();
        float y = vAlign switch
        {
            VAlign.Top => boxTop - ascent,
            VAlign.Bottom => boxTop + boxHeight - descent,
            _ => boxTop + boxHeight / 2 - (ascent + descent) / 2,
        };

        canvas.DrawText(text, x, y, paint);
    }
}
