using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Decoding;
using WheelTalk.Core.Settings.Device;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Страница 8 кадра Veteran — настройки колеса (план 34, этап 1). Кадры настоящие: те самые, что
/// Sherman L прислал 28.07.2026, собранные из записи тем же распаковщиком, что работает в бою.
/// Синтетика встречается дважды — там, где нужного байта в записи просто нет (знаковая поправка
/// напряжения и короткий кадр); оба раза это настоящий кадр с подменённым байтом, и это отмечено
/// в самом тесте.
/// </summary>
public class VeteranSettingsPageTests
{
    /// <summary>Эталон плана 34 §1.4, столбец «Sherman L»: все шестнадцать полей раскладки.
    /// Через всю поездку они не меняются — колесо настраивали до записи, а не во время.</summary>
    private static readonly (string Key, int Value)[] ShermanL =
    [
        (WheelSettingKeys.PedalHardness, 94),
        (WheelSettingKeys.StopSpeed, 200),
        (WheelSettingKeys.StopPowerRate, 94),
        (WheelSettingKeys.ScreenBacklightRate, 30),
        (WheelSettingKeys.Gyro, 0),
        (WheelSettingKeys.TransportMode, 0),
        (WheelSettingKeys.Unit, 0),
        (WheelSettingKeys.Vol, 0),
        (WheelSettingKeys.LowVolMode, 0),
        (WheelSettingKeys.HighSpeedMode, 1),
        (WheelSettingKeys.KeyTone, 8),
        (WheelSettingKeys.MaxChargeVol, 65),
        (WheelSettingKeys.MaxChargeVolBase, 145),
        (WheelSettingKeys.UpOrDownSpeedHelper, 45),
        (WheelSettingKeys.UpSpeedCul, 60),
    ];

    private static readonly DateTimeOffset Received = new(2026, 7, 28, 0, 47, 44, TimeSpan.Zero);

    [Fact]
    public void The_first_settings_frame_of_the_ride_gives_all_sixteen_values()
    {
        var frames = SettingsFrames();
        Assert.Equal(30, frames.Count); // две минуты поездки, страница раз в 4 секунды

        var snapshot = VeteranSettingsPage.Parse(frames[0], Received);

        Assert.NotNull(snapshot);
        Assert.Equal(Received, snapshot.ReceivedAt);
        Assert.Equal(16, snapshot.Values.Count);
        foreach ((string key, int expected) in ShermanL)
        {
            var value = snapshot[key];
            Assert.True(value.Supported, $"{key}: колесо должно было сообщить значение");
            Assert.Equal(expected, value.Value);
        }

        // Шестнадцатое поле — то, которого у этого колеса нет; оно проверено отдельно ниже.
        Assert.False(snapshot[WheelSettingKeys.BrakePressureAlarm].Supported);
    }

    /// <summary>Сентинел <c>0x80</c>: у Sherman L нет тревоги по давлению тормоза, и колесо
    /// говорит об этом прямо. Сырой байт при этом сохраняется — по нему и принято решение.</summary>
    [Fact]
    public void A_setting_the_wheel_does_not_have_comes_back_unsupported()
    {
        var snapshot = VeteranSettingsPage.Parse(SettingsFrames()[0], Received);

        Assert.NotNull(snapshot);
        var brake = snapshot[WheelSettingKeys.BrakePressureAlarm];
        Assert.False(brake.Supported);
        Assert.Equal((byte?)0x80, brake.Raw);
    }

    /// <summary>
    /// Капкан К1. Поправка напряжения — единственное знаковое поле страницы, и строка,
    /// скопированная у соседа, дала бы 241 вместо −15.
    /// <para><b>Кадр синтетический:</b> настоящий кадр записи с подменённым байтом 59 — минус
    /// пятнадцати в поездке не было, колесо стояло на нуле.</para>
    /// </summary>
    [Fact]
    public void The_voltage_correction_is_read_as_a_signed_byte()
    {
        byte[] frame = SettingsFrames()[0];
        frame[59] = 0xF1;

        var snapshot = VeteranSettingsPage.Parse(frame, Received);

        Assert.NotNull(snapshot);
        var vol = snapshot[WheelSettingKeys.Vol];
        Assert.True(vol.Supported);
        Assert.Equal(-15, vol.Value);
        Assert.Equal((byte?)0xF1, vol.Raw);
    }

    /// <summary>
    /// Капкан К2: у знакового поля <c>0x80</c> — это и «настройки нет», и законное −128. Решает
    /// сырой байт, поэтому знаковость поля на ответ не влияет.
    /// <para><b>Кадр синтетический:</b> настоящий кадр записи с подменённым байтом 59.</para>
    /// </summary>
    [Fact]
    public void The_sentinel_wins_over_the_signed_reading()
    {
        byte[] frame = SettingsFrames()[0];
        frame[59] = 0x80;

        var snapshot = VeteranSettingsPage.Parse(frame, Received);

        Assert.NotNull(snapshot);
        var vol = snapshot[WheelSettingKeys.Vol];
        Assert.False(vol.Supported);
        Assert.Equal((byte?)0x80, vol.Raw);
    }

    /// <summary>
    /// Капкан К5: прошивка вправе прислать кадр короче раскладки, и чтение за границей уронило бы
    /// разбор посреди поездки. О чём кадр не сказал — того нет, без исключения и без нулей,
    /// выданных за настройку.
    /// <para><b>Кадры синтетические:</b> настоящий кадр записи, обрезанный до 60 и до 50 байт.</para>
    /// </summary>
    [Fact]
    public void A_frame_that_ends_early_reports_the_missing_fields_as_unsupported()
    {
        byte[] full = SettingsFrames()[0];

        var snapshot = VeteranSettingsPage.Parse(full[..60], Received);

        Assert.NotNull(snapshot);
        Assert.True(snapshot[WheelSettingKeys.PedalHardness].Supported);
        Assert.True(snapshot[WheelSettingKeys.Vol].Supported); // байт 59 — последний, до которого кадр дотянулся
        foreach (string key in new[]
                 {
                     WheelSettingKeys.LowVolMode, WheelSettingKeys.HighSpeedMode, WheelSettingKeys.KeyTone,
                     WheelSettingKeys.MaxChargeVol, WheelSettingKeys.MaxChargeVolBase,
                     WheelSettingKeys.UpOrDownSpeedHelper, WheelSettingKeys.UpSpeedCul,
                     WheelSettingKeys.BrakePressureAlarm,
                 })
        {
            var value = snapshot[key];
            Assert.False(value.Supported, $"{key}: кадр до этого поля не дотянулся");
            Assert.Null(value.Raw);
        }

        // Кадр, не дотянувшийся даже до первого поля, — это не пустой снимок, а его отсутствие:
        // прежние настройки на экране лучше шестнадцати прочерков.
        Assert.Null(VeteranSettingsPage.Parse(full[..50], Received));
    }

    /// <summary>
    /// Шаг 1.6. Страницы 0–7 — банки и температуры BMS; снимка настроек они не касаются ни до
    /// первого кадра страницы 8, ни после него.
    /// </summary>
    [Fact]
    public void Frames_of_the_bms_pages_leave_the_settings_alone()
    {
        var harness = DecoderHarness.ForVeteran();
        var frames = AllFrames();
        var bmsFrames = frames.Where(IsBmsPage).Take(20).ToList();
        Assert.Equal(20, bmsFrames.Count);

        foreach (byte[] frame in bmsFrames.Take(10)) harness.Decoder.Feed(frame);
        Assert.Null(harness.Snapshot().WheelSettings);

        harness.Decoder.Feed(frames.First(IsSettingsPage));
        var settings = harness.Snapshot().WheelSettings;
        Assert.NotNull(settings);

        foreach (byte[] frame in bmsFrames.Skip(10)) harness.Decoder.Feed(frame);
        Assert.Same(settings, harness.Snapshot().WheelSettings);
    }

    /// <summary>
    /// Шаг 1.6, вторая половина: кадр страницы 8 — обычный кадр телеметрии, и разбирается он как
    /// любой другой. Числа — из байтов того самого кадра.
    /// </summary>
    [Fact]
    public void A_settings_frame_decodes_its_telemetry_like_any_other()
    {
        var harness = DecoderHarness.ForVeteran(config => config.GotwayNegative = "0");

        harness.Decoder.Feed(AllFrames().First(IsSettingsPage));

        var snapshot = harness.Snapshot();
        Assert.Equal("Sherman L", snapshot.Model);
        Assert.Equal("006.0.10", snapshot.Version);
        Assert.Equal(146.91, snapshot.VoltageV, 2);
        Assert.Equal(1.70, snapshot.SpeedKmh, 2);
        Assert.Equal(21.80, snapshot.PhaseCurrentA, 2);
        Assert.Equal(43, snapshot.TemperatureC);
        Assert.NotNull(snapshot.WheelSettings);
    }

    private static bool IsSettingsPage(byte[] frame) =>
        frame.Length > 46 && frame[46] == VeteranSettingsPage.PageNumber;

    private static bool IsBmsPage(byte[] frame) =>
        frame.Length > 46 && frame[46] < VeteranSettingsPage.PageNumber;

    private static List<byte[]> SettingsFrames() => AllFrames().Where(IsSettingsPage).ToList();

    /// <summary>
    /// Кадры записи, собранные боевым распаковщиком: заголовок, длина, тело, проверенный CRC —
    /// ровно то, что видит <c>VeteranDecoder.Decode</c>, а не байты, нарезанные тестом на глаз.
    /// </summary>
    private static List<byte[]> AllFrames()
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
