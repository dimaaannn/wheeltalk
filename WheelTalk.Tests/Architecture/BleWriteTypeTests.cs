using System.Text.RegularExpressions;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Architecture;

/// <summary>
/// Замок плана 36 Л2: <b>InMotion пишется с подтверждением, остальные марки — без</b>.
/// <para>
/// Почему так. DarknessBot делит тип записи ровно по этим линкам, и InMotion — единственная марка,
/// которую он выделил из пяти: все 51 запись нового адаптера и 13 старого идут с подтверждением,
/// причём именованным аргументом у каждого вызова, а не умолчанием (мастер-план §8а). Так выглядит
/// след чужих грабель. Подтверждаемая запись сама держит темп — следующая не уйдёт, пока колесо не
/// ответило, — и потому это кандидат №1 в причину отвала V14. У нас тип записи стоял литералом
/// <c>NoResponse</c> в двух местах <c>BeginWrite</c>.
/// </para>
/// <para>
/// Читается по исходнику: android-проекты тестам не видны, а живого GATT в тесте нет и быть не
/// может — тип записи проверяется только на колесе (<c>polling-architecture-review.md</c> §6.3).
/// Но подмена типа на литерал обратно — правка одной строки, и её ловит эта проверка.
/// </para>
/// </summary>
public class BleWriteTypeTests
{
    private const string Source = "WheelTalk.Droid/Ble/AndroidBleClient.cs";

    /// <summary>
    /// Линк → тип записи, ветвь в ветвь с DarknessBot. <c>Default</c> у Android — это и есть запись
    /// <b>с подтверждением</b> (ATT Write Request); имя со словом «Response» носит противоположный
    /// тип, и на нём спотыкаются.
    /// </summary>
    [Fact]
    public void Inmotion_links_are_written_with_confirmation_and_the_others_without()
    {
        string body = RepoFiles.MethodBody(
            RepoFiles.Read(Source), "private static CharacteristicPair? SelectCharacteristics(BluetoothGatt gatt)");

        var pairs = Regex.Matches(body, @"new CharacteristicPair\((\w+), (\w+), GattWriteType\.(\w+)\)")
            .Select(m => (Notify: m.Groups[1].Value, Write: m.Groups[2].Value, Type: m.Groups[3].Value))
            .ToList();

        Assert.Equal(
        [
            ("ffe1", "ffe1", "NoResponse"),          // Begode, Veteran, KingSong
            ("ffe4", "ffe9", "Default"),             // InMotion V1
            ("nordicNotify", "nordicWrite", "Default"), // InMotion V2
        ], pairs);
    }

    /// <summary>
    /// Сама запись читает тип у линка, а не называет его литералом — иначе деление выше не значит
    /// ничего. Оба пути (API 33+ и старый) обязаны идти одним типом.
    /// </summary>
    [Fact]
    public void The_write_itself_reads_the_link_type_instead_of_naming_one()
    {
        string body = RepoFiles.MethodBody(RepoFiles.Read(Source), "private bool BeginWrite(byte[] cmd)");

        Assert.Contains("var writeType = _writeType;", body);
        Assert.Equal(2, Regex.Matches(body, @"\bwriteType\b(?!\s*=)").Count);
        Assert.DoesNotContain("GattWriteType.", body);
    }

    /// <summary>
    /// Подтверждаемую запись просят только у характеристики, которая её объявляет: у колеса с
    /// урезанным профилем такая запись не пройдёт вовсе, и команды пропали бы молча.
    /// </summary>
    [Fact]
    public void Confirmed_writes_are_only_asked_of_a_characteristic_that_declares_them()
    {
        string source = RepoFiles.Read(Source);
        string body = RepoFiles.MethodBody(source,
            "private static GattWriteType SupportedWriteType(BluetoothGattCharacteristic write, GattWriteType wanted, ILogger logger)");

        Assert.Contains("GattProperty.Write", body);
        Assert.Contains("return GattWriteType.NoResponse;", body);

        // И он же стоит на пути: тип линка ставится через него, а не мимо.
        Assert.Contains("client._writeType = SupportedWriteType(", source);
    }
}
