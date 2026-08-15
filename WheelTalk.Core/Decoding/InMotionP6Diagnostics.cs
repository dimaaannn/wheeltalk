namespace WheelTalk.Core.Decoding;

/// <summary>Итог разбора одного кадра диагностики: слова для <see cref="WheelState.SetAlert"/> и
/// признак бита сверх известных 45 — сигнал журналу, не повод падать.</summary>
internal readonly record struct InMotionP6DiagnosticsResult(string AlertText, bool HasUnknownBit);

/// <summary>
/// Разбор подкоманды диагностики InMotion (<c>Diagnostic</c>, subcmd 3) по раскладке
/// <see cref="InMotionP6DiagnosticFlags"/>. Не порт — <c>InMotionDecoderV2</c> эту подкоманду
/// распознаёт и отбрасывает (<c>Command.Diagnostic =&gt; return false</c>), для всей линейки V2;
/// здесь — обходной путь только для P6, разобранный офлайн по раскладке производителя.
/// <para>
/// Payload несёт двойную нагрузку: первые 4 байта — это одновременно <c>errorCode</c> (не нужен
/// тревогам, свой для будущего экрана диагностики) и первые 32 из 45 битовых флагов. Флаг с индексом
/// <c>i</c> живёт в <c>data[i/8]</c>, бит <c>i%8</c> — то же чтение двумя способами.
/// </para>
/// </summary>
internal static class InMotionP6Diagnostics
{
    private static readonly IReadOnlyList<InMotionDiagnosticFlag> Flags = InMotionP6DiagnosticFlags.All;

    /// <summary>Байт, которого <paramref name="data"/> не хватает, читается нулём — короткий payload
    /// не тревога сам по себе, а «эти биты не пришли».</summary>
    private static byte ByteAt(byte[] data, int index) => index < data.Length ? data[index] : (byte)0;

    public static InMotionP6DiagnosticsResult Decode(byte[] data)
    {
        var words = new List<string>();
        for (int i = 0; i < Flags.Count; i++)
        {
            byte b = ByteAt(data, i / 8);
            if (((b >> (i % 8)) & 1) == 0) continue;

            var flag = Flags[i];
            words.Add($"{flag.Severity}: {flag.Title}");
        }

        return new InMotionP6DiagnosticsResult(string.Join("; ", words), HasUnknownBit(data));
    }

    /// <summary>Биты за пределами известных 45 — три верхних бита последнего известного байта плюс
    /// любой байт дальше него. Раскладка производителя доказана только на первых 45.</summary>
    private static bool HasUnknownBit(byte[] data)
    {
        int lastKnownByte = (Flags.Count - 1) / 8;
        int usedBitsInLastByte = Flags.Count - lastKnownByte * 8;
        byte tail = (byte)(ByteAt(data, lastKnownByte) >> usedBitsInLastByte);
        if (tail != 0) return true;

        for (int i = lastKnownByte + 1; i < data.Length; i++)
        {
            if (data[i] != 0) return true;
        }
        return false;
    }
}
