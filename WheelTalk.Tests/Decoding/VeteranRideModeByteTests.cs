using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Settings.Device;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Байт 31 кадра телеметрии Veteran — режим езды (план 34, шаг 4.2; отклонение записано в
/// <c>docs/port-deviations.md</c>). Оригинал эти байты читает и выбрасывает
/// (<c>VeteranAdapter.java:51</c>), мы доводим байт до состояния — сырым, без толкования.
/// <para>
/// Кадры настоящие: запись Sherman L 28.07.2026, собранная боевым распаковщиком с проверкой CRC,
/// и портированные hex-фикстуры оригинала (Abrams, Patton) — те же, что в
/// <see cref="VeteranDecoderTests"/>.
/// </para>
/// </summary>
public class VeteranRideModeByteTests
{
    /// <summary>
    /// Старое колесо с тремя положениями педалей: Abrams шлёт 3 — «Strong» по родному приложению
    /// (<c>HomepageFragment.java:324</c>, массив <c>ride_mode</c>). Значение наверх идёт как есть.
    /// </summary>
    [Fact]
    public void An_old_wheel_reports_one_of_the_three_ride_mode_positions()
    {
        var harness = DecoderHarness.ForVeteran();

        harness.FeedHex(
            "dc5a5c20266d00004aaf00004aaf000000000d9e",
            "0b8800000af00af007d2000300050004");

        Assert.Equal("002.0.02", harness.Snapshot().Version); // тот самый кадр Abrams, не чужой
        Assert.Equal((byte?)3, harness.Snapshot().RideModeRaw);
    }

    /// <summary>Patton (версия протокола 4) шлёт 2 в том же байте — значение читается из кадра, а
    /// не подобрано под одно колесо.</summary>
    [Fact]
    public void Patton_reports_its_own_value_in_the_same_byte()
    {
        var harness = DecoderHarness.ForVeteran();

        harness.FeedHex(
            "dc5a5c452abe00003edc00008562003500000b5c",
            "0dfe000002bc07d00fac000219fb0000006f0000",
            "80808080808004000014ffffffffff32ee029109",
            "df0fd303cb000000006f9a79c2");

        Assert.Equal("004.0.12", harness.Snapshot().Version);
        Assert.Equal((byte?)2, harness.Snapshot().RideModeRaw);
    }

    /// <summary>
    /// Читается <b>один байт 31</b>, а не 16-битное слово с 30, как в оригинале: байт 30 — старшая
    /// часть кода версии и законно бывает 0x07 (<c>BtManager.java:372</c>, <c>VeteranUnpacker.cs:51</c>).
    /// Слово дало бы 0x0703 = 1795 вместо 3.
    /// <para><b>Кадр синтетический:</b> тот же кадр Abrams с байтом 30, поднятым до 0x07. CRC у
    /// него нет — длина 32 байта, старый формат, — поэтому подмена законна.</para>
    /// </summary>
    [Fact]
    public void The_version_byte_next_door_does_not_leak_into_the_ride_mode()
    {
        var harness = DecoderHarness.ForVeteran();

        harness.FeedHex(
            "dc5a5c20266d00004aaf00004aaf000000000d9e",
            "0b8800000af00af007d2070300050004");

        Assert.Equal((byte?)3, harness.Snapshot().RideModeRaw);
    }

    /// <summary>Пока кадра телеметрии не было, сказать о режиме нечего — и это не ноль.</summary>
    [Fact]
    public void Before_the_first_frame_there_is_no_ride_mode_at_all()
    {
        Assert.Null(DecoderHarness.ForVeteran().Snapshot().RideModeRaw);
    }

    /// <summary>
    /// Sherman L: 0x80 во <b>всех</b> кадрах поездки — то же значение, каким страница 8 говорит
    /// «такой настройки у этого колеса нет». Плавную жёсткость педалей это колесо сообщает
    /// страницей 8 (94, план 34 §1.4), и две половины сходятся: где есть плавная шкала, там байт
    /// 31 пуст. Толковать 0x80 как режим здесь намеренно не пробуем — это работа этапа 4.
    /// </summary>
    [Fact]
    public void Sherman_l_sends_the_no_such_setting_value_in_every_frame_of_the_ride()
    {
        var frames = RideFrames();
        Assert.Equal(597, frames.Count);

        var harness = DecoderHarness.ForVeteran();
        foreach (byte[] frame in frames)
        {
            harness.Decoder.Feed(frame);
            Assert.Equal((byte?)0x80, harness.Snapshot().RideModeRaw);
        }

        var settings = harness.Snapshot().WheelSettings;
        Assert.NotNull(settings);
        Assert.True(settings[WheelSettingKeys.PedalHardness].Supported);
        Assert.Equal(94, settings[WheelSettingKeys.PedalHardness].Value);
    }

    /// <summary>
    /// Кадры записи, собранные боевым распаковщиком: заголовок, длина, тело, проверенный CRC —
    /// ровно то, что видит <c>VeteranDecoder.Decode</c>, а не байты, нарезанные тестом на глаз.
    /// </summary>
    private static List<byte[]> RideFrames()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "shermanl_raw_ride_20260728.csv");

        var unpacker = new VeteranUnpacker(NullLogger<VeteranDecoder>.Instance);
        var frames = new List<byte[]>();
        foreach (string line in File.ReadLines(path))
        {
            int comma = line.IndexOf(',');
            if (comma < 0) continue;

            foreach (byte b in Convert.FromHexString(line[(comma + 1)..].Trim()))
            {
                if (unpacker.AddChar(b)) frames.Add(unpacker.GetBuffer());
            }
        }

        return frames;
    }
}
