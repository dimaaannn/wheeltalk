using System.Globalization;

namespace WheelTalk.Droid.Resources.Strings;

/// <summary>
/// Согласование существительного с числом. Русскому нужны три формы там, где английскому две, и
/// одна строка ресурса выразить их не может: «3 настроек» и «4 настроек» неверны так же, как
/// «1 настроек».
/// <para>
/// Формы берутся тремя ключами ресурса подряд — <c>…1</c>, <c>…2</c>, <c>…5</c>, по числу, при
/// котором форма впервые встречается: 1 настройка, 2 настройки, 5 настроек. Именование числами, а не
/// словами вроде <c>Few</c>: правило само сформулировано в числах, и переводчику видно, что
/// подставить.
/// </para>
/// </summary>
internal static class Plural
{
    /// <summary>
    /// Русское правило целиком. Одиннадцать — не «одна», а девяносто один — «одна»: решают
    /// последняя цифра и то, не попало ли число в десяток от 11 до 14, где все формы последние.
    /// </summary>
    public static string Of(int count, string one, string few, string many)
    {
        string form = (count % 100 is >= 11 and <= 14) ? many
            : (count % 10) switch
            {
                1 => one,
                2 or 3 or 4 => few,
                _ => many,
            };

        return string.Format(CultureInfo.CurrentCulture, form, count);
    }
}
