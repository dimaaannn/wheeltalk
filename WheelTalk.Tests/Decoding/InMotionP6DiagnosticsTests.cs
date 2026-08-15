using WheelTalk.Core.Decoding;

namespace WheelTalk.Tests.Decoding;

/// <summary>
/// Синтетические замки на разборе подкоманды диагностики (<see cref="InMotionP6Diagnostics"/>) — по
/// раскладке <c>docs/originals-reference-data.md</c> §6, 45 битовых флагов. Живого дампа диагностики
/// P6 нет (этап 0 за владельцем); байты собраны по номеру флага <c>i</c> из самой раскладки
/// (<c>byteIndex = i/8, bitIndex = i%8</c>), не из чужого разбора.
/// </summary>
public class InMotionP6DiagnosticsTests
{
    /// <summary>Флаг #0 — байт 0, бит 0: "Phase current sensor fault", Error.</summary>
    [Fact]
    public void Single_flag_becomes_its_own_alert()
    {
        var result = InMotionP6Diagnostics.Decode([0x01]);

        Assert.Equal("Error: Phase current sensor fault", result.AlertText);
        Assert.False(result.HasUnknownBit);
    }

    /// <summary>
    /// Флаг #0 (байт 0, бит 0, Error) вместе с флагом #30 (байт 3, бит 6: "Overspeed", Warning) —
    /// оба уходят в одну строку, тяжесть каждого своя, порядок по номеру флага.
    /// </summary>
    [Fact]
    public void Several_flags_join_with_their_own_severity()
    {
        byte[] data = [0x01, 0x00, 0x00, 0x40];

        var result = InMotionP6Diagnostics.Decode(data);

        Assert.Equal("Error: Phase current sensor fault; Warning: Overspeed", result.AlertText);
        Assert.False(result.HasUnknownBit);
    }

    /// <summary>Пустая подкоманда — тишина, не «нет данных, оставить как было».</summary>
    [Fact]
    public void Empty_subcommand_is_silence()
    {
        var result = InMotionP6Diagnostics.Decode([]);

        Assert.Equal("", result.AlertText);
        Assert.False(result.HasUnknownBit);
    }

    /// <summary>
    /// Флаг #44 — последний доказанный (байт 5, бит 4: "Fan speed too low", Warning) читается как
    /// обычно; следующий по счёту (бит 5 того же байта) раскладкой не доказан.
    /// </summary>
    [Fact]
    public void Last_proven_flag_decodes_normally()
    {
        var result = InMotionP6Diagnostics.Decode([0, 0, 0, 0, 0, 0x10]);

        Assert.Equal("Warning: Fan speed too low", result.AlertText);
        Assert.False(result.HasUnknownBit);
    }

    /// <summary>Бит 5 байта 5 — сразу за последним доказанным флагом (#44). Не падаем, в тревогу
    /// не идёт (раскладка его не знает), но признак для журнала поднимается.</summary>
    [Fact]
    public void Bit_right_after_the_proven_45_is_flagged_not_decoded()
    {
        var result = InMotionP6Diagnostics.Decode([0, 0, 0, 0, 0, 0x20]);

        Assert.Equal("", result.AlertText);
        Assert.True(result.HasUnknownBit);
    }

    /// <summary>Байт целиком за пределами раскладки (индекс 6+) — тот же случай, другая форма.</summary>
    [Fact]
    public void Byte_beyond_the_known_layout_is_flagged_not_decoded()
    {
        var result = InMotionP6Diagnostics.Decode([0, 0, 0, 0, 0, 0, 0xFF]);

        Assert.Equal("", result.AlertText);
        Assert.True(result.HasUnknownBit);
    }

    /// <summary>Короткий payload — недостающие байты читаются нулём, не бросают исключение.</summary>
    [Fact]
    public void Short_payload_treats_missing_bytes_as_zero()
    {
        var result = InMotionP6Diagnostics.Decode([0]);

        Assert.Equal("", result.AlertText);
        Assert.False(result.HasUnknownBit);
    }

    /// <summary>Таблица целиком: 45 записей, порядок — индекс флага, тяжесть — из источника, не
    /// выведена по имени (грабля мастер-плана: "имя не доказательство").</summary>
    [Fact]
    public void Flag_table_has_all_45_entries_in_index_order()
    {
        Assert.Equal(45, InMotionP6DiagnosticFlags.All.Count);
        Assert.Equal("Phase current sensor fault", InMotionP6DiagnosticFlags.All[0].Title);
        Assert.Equal(InMotionDiagnosticSeverity.Error, InMotionP6DiagnosticFlags.All[0].Severity);
        Assert.Equal("Critically low battery", InMotionP6DiagnosticFlags.All[20].Title);
        Assert.Equal(InMotionDiagnosticSeverity.Warning, InMotionP6DiagnosticFlags.All[20].Severity);
        Assert.Equal("Fan speed too low", InMotionP6DiagnosticFlags.All[44].Title);
        Assert.Equal(InMotionDiagnosticSeverity.Warning, InMotionP6DiagnosticFlags.All[44].Severity);
    }
}
