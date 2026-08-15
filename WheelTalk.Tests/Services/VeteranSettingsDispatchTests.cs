using Microsoft.Extensions.Logging.Abstractions;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Services;
using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Services;

/// <summary>
/// Разводка новых команд записи настроек в <see cref="WheelService.SendCommand"/> — план импорта
/// команд §2.5. Проверяется не содержимое кадра (это делают байтовые тесты), а три вещи, которых
/// байтовый тест не видит: команда доходит до нужного билдера, негодное значение до колеса не
/// доезжает вовсе, и чужому протоколу такая команда не отдаётся ни при каких обстоятельствах.
/// </summary>
public class VeteranSettingsDispatchTests
{
    private static (WheelService Service, FakeTransport Transport) BuildVeteran()
    {
        var harness = DecoderHarness.ForVeteran();
        var transport = new FakeTransport();
        return (new WheelService(transport, harness.Decoder, NullLogger<WheelService>.Instance), transport);
    }

    public static TheoryData<WheelCommand, string> RoutedCommands() => new()
    {
        { new WheelCommand.SetUnitSystem(true), "4C6441701701028080808080808080808080011FF96E85" },
        { new WheelCommand.SetHighSpeedMode(true), "4C6441701A01028080808080808080808080808080012C5FA11F" },
        { new WheelCommand.SetKeyToneVolume(100), "4C6441701C0102808080808080808080808080808080806438DA7228" },
        { new WheelCommand.SetMaxChargeVoltage(120), "4C6441701D01028080808080808080808080808080808080781C99D17D" },
        { new WheelCommand.SetAccelerationHelper(50), "4C6441701F010280808080808080808080808080808080808080328F89D3D5" },
        { new WheelCommand.SetAccelerationReduction(0), "4C64417021010280808080808080808080808080808080808080808000F0FC0BD4" },
        { new WheelCommand.SetBrakeOverpressureAlarm(125), "4C644170220102808080808080808080808080808080808080808080807DB42C7D79" },
        { new WheelCommand.SetVoltageCorrection(-15), "4C644170180102808080808080808080808080F129076DF6" },
        { new WheelCommand.SetStopSpeed(120), "4C644170110102808080808078C4715523" },
        { new WheelCommand.SetStopPower(30), "4C6441701201028080808080801EBDFC027F" },
        { new WheelCommand.SetScreenBacklight(100), "4C6441701401028080808080808080646E9CA606" },
        // Тревога скорости — пара кадров одним буфером, ровно как её кладёт в очередь производитель.
        { new WheelCommand.SetSpeedAlarm(30), "4C6B417011018080808080801E0DBDF5E44C64417011010080808080801EF73F8067" },
    };

    [Theory]
    [MemberData(nameof(RoutedCommands))]
    public async Task Each_settings_command_reaches_the_wire(WheelCommand command, string expectedHex)
    {
        var (service, transport) = BuildVeteran();

        await service.SendCommand(command);

        Assert.Equal(expectedHex, Convert.ToHexString(Assert.Single(transport.Written)));
    }

    /// <summary>Значение вне диапазона производителя обрывается на билдере: писать нечего, и
    /// <c>WheelService</c> уходит по своей штатной ветке «команда пропущена».</summary>
    [Fact]
    public async Task Out_of_range_value_never_reaches_the_wire()
    {
        var (service, transport) = BuildVeteran();

        await service.SendCommand(new WheelCommand.SetStopPower(101));
        await service.SendCommand(new WheelCommand.SetBrakeOverpressureAlarm(79));

        Assert.Empty(transport.Written);
    }

    /// <summary>
    /// Другому протоколу настройки Veteran не отдаются: примерка <c>as IVeteranSettingsCommands</c>
    /// не срастается, и команда молча пропускается — а не строится чужим билдером с тем же именем.
    /// </summary>
    [Fact]
    public async Task A_non_Veteran_decoder_gets_nothing()
    {
        var harness = DecoderHarness.ForGotway();
        var transport = new FakeTransport();
        var service = new WheelService(transport, harness.Decoder, NullLogger<WheelService>.Instance);

        await service.SendCommand(new WheelCommand.SetUnitSystem(true));
        await service.SendCommand(new WheelCommand.SetStopSpeed(30));

        Assert.Empty(transport.Written);
    }
}
