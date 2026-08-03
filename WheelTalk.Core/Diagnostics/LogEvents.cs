namespace WheelTalk.Core.Diagnostics;

/// <summary>
/// Centralized event-id catalog for the Core decode pipeline, grouped by numeric range so a log
/// sink/query can filter by area without depending on message text. Exposed as plain
/// <c>const int</c>/<c>const string</c> pairs (rather than <see cref="Microsoft.Extensions.Logging.EventId"/>
/// instances) because <see cref="Microsoft.Extensions.Logging.LoggerMessageAttribute"/> requires
/// compile-time constants for its <c>EventId</c>/<c>EventName</c> properties.
///   1xxx — service / lifecycle (<see cref="Services.WheelService"/>, and the protocol decoders'
///          own write-initiated commands, which are the same conceptual event from a different origin).
///   2xxx — frame unpacking (<see cref="Decoding.GotwayUnpacker"/> / <see cref="Decoding.VeteranUnpacker"/>
///          byte-assembly automatons).
///   3xxx — protocol decoding (<see cref="Services.Decoder"/>, <see cref="Decoding.GotwayDecoder"/>,
///          <see cref="Decoding.VeteranDecoder"/>, <see cref="Decoding.KingsongDecoder"/> frame interpretation).
/// </summary>
public static class LogEvents
{
    /// <summary>1xxx — service / lifecycle.</summary>
    public static class Service
    {
        public const int CmdSentId = 1001;
        public const string CmdSentName = "Cmd.Sent";

        public const int CmdSkippedId = 1002;
        public const string CmdSkippedName = "Cmd.Skipped";

        public const int ProtocolWriteFailedId = 1003;
        public const string ProtocolWriteFailedName = "ProtocolWriteFailed";

        public const int ProtocolSelectedId = 1004;
        public const string ProtocolSelectedName = "ProtocolSelected";

        /// <summary>Gotway/Begode-specific: a delayed follow-up command (queued via <c>DelayedSend</c>)
        /// failed to send. Distinct from <see cref="ProtocolWriteFailedId"/>, which covers
        /// <see cref="Services.WheelService"/>'s relay of protocol-initiated writes.</summary>
        public const int DelayedSendFailedId = 1005;
        public const string DelayedSendFailedName = "DelayedSendFailed";

        /// <summary>A user-initiated command (<see cref="Services.WheelService.SendCommand"/>) never made
        /// it to the wheel — the transport's write threw (refused immediately, or never confirmed).
        /// Distinct from <see cref="CmdSentId"/>, which now only fires once delivery is confirmed.</summary>
        public const int CmdFailedId = 1006;
        public const string CmdFailedName = "Cmd.Failed";

        /// <summary>Same <c>Cmd.Sent</c> event as <see cref="CmdSentId"/> — same id, same rendered
        /// message prefix — for a write the *decoder* initiated (Begode's handshake polling, a
        /// two-step command's delayed half) rather than a <see cref="Contracts.WheelCommand"/> a
        /// caller asked for. A distinct EventName only because <c>LoggerMessage</c> requires unique
        /// names within one class; the two log methods used to live in different classes and shared
        /// the name without conflict.</summary>
        public const int CmdSentProtocolId = CmdSentId;
        public const string CmdSentProtocolName = "Cmd.SentProtocol";

        /// <summary>Опрос колеса не доехал, потому что связи уже нет
        /// (<see cref="Ports.WriteLinkLostException"/>). Отдельно от
        /// <see cref="ProtocolWriteFailedId"/> и уровнем ниже: сам обрыв записан в журнал тем, кто
        /// его заметил, а декодер опрашивает колесо двадцать раз в секунду — красная строка с
        /// трассировкой на каждый его такт хоронит настоящую причину под собой.</summary>
        public const int ProtocolWriteAbandonedId = 1007;
        public const string ProtocolWriteAbandonedName = "ProtocolWriteAbandoned";
    }

    /// <summary>2xxx — frame unpacking (byte-assembly automatons).</summary>
    public static class Unpacking
    {
        public const int HeaderFoundId = 2001;
        public const string HeaderFoundName = "HeaderFound";

        public const int FrameValidId = 2002;
        public const string FrameValidName = "FrameValid";

        public const int InvalidFooterId = 2003;
        public const string InvalidFooterName = "InvalidFooter";

        public const int GarbageReassembledId = 2004;
        public const string GarbageReassembledName = "GarbageReassembled";

        public const int CrcOkId = 2005;
        public const string CrcOkName = "CrcOk";

        public const int CrcFailId = 2006;
        public const string CrcFailName = "CrcFail";

        public const int LenVerifyFailedId = 2007;
        public const string LenVerifyFailedName = "LenVerifyFailed";

        /// <summary>Veteran-specific: a complete frame was assembled (length matched, step reset).
        /// Distinct from <see cref="FrameValidId"/>, which covers Gotway/Begode's own "valid frame"
        /// event — the two protocols' unpackers previously shared an EventId despite meaning
        /// different things.</summary>
        public const int VeteranFrameValidId = 2008;
        public const string VeteranFrameValidName = "VeteranFrameValid";

        /// <summary>InMotion-specific: a complete CAN frame was assembled (AA AA … 55 55, escape
        /// bytes removed) and is ready for <see cref="Decoding.InMotionCanMessage.Verify"/>.</summary>
        public const int InMotionFrameValidId = 2009;
        public const string InMotionFrameValidName = "InMotionFrameValid";

        /// <summary>InMotion-specific: an assembled frame's checksum did not match — discarded, same
        /// as the original's own silent drop (<c>CANMessage.verify</c> returning <c>null</c>).</summary>
        public const int InMotionChecksumFailId = 2010;
        public const string InMotionChecksumFailName = "InMotionChecksumFail";

        /// <summary>InMotion V2-specific: a complete frame was assembled (AA AA + length-delimited
        /// body, no footer marker — see <see cref="Decoding.InMotionV2Unpacker"/>'s class doc).</summary>
        public const int InMotionV2FrameValidId = 2011;
        public const string InMotionV2FrameValidName = "InMotionV2FrameValid";

        public const int InMotionV2ChecksumFailId = 2012;
        public const string InMotionV2ChecksumFailName = "InMotionV2ChecksumFail";
    }

    /// <summary>3xxx — protocol decoding (frame interpretation).</summary>
    public static class Decoding
    {
        public const int DecodeInvokedId = 3001;
        public const string DecodeInvokedName = "DecodeInvoked";

        public const int FrameAId = 3002;
        public const string FrameAName = "FrameA";

        public const int FrameBId = 3003;
        public const string FrameBName = "FrameB";

        public const int Frame01Id = 3004;
        public const string Frame01Name = "Frame01";

        public const int Frame07Id = 3005;
        public const string Frame07Name = "Frame07";

        public const int BmsCellsId = 3006;
        public const string BmsCellsName = "BmsCells";

        public const int HandshakeId = 3007;
        public const string HandshakeName = "Handshake";

        public const int WheelAlertId = 3008;
        public const string WheelAlertName = "WheelAlert";

        public const int FrameReceivedId = 3009;
        public const string FrameReceivedName = "FrameReceived";

        public const int FrameDecodedId = 3010;
        public const string FrameDecodedName = "FrameDecoded";

        public const int KsLiveDataId = 3011;
        public const string KsLiveDataName = "KsLiveData";

        public const int KsDistanceTimeFanId = 3012;
        public const string KsDistanceTimeFanName = "KsDistanceTimeFan";

        public const int KsCpuLoadId = 3013;
        public const string KsCpuLoadName = "KsCpuLoad";

        public const int KsSpeedLimitId = 3014;
        public const string KsSpeedLimitName = "KsSpeedLimit";

        public const int ImFastInfoId = 3015;
        public const string ImFastInfoName = "ImFastInfo";

        public const int ImSlowInfoId = 3016;
        public const string ImSlowInfoName = "ImSlowInfo";

        public const int ImAlertId = 3017;
        public const string ImAlertName = "ImAlert";

        public const int ImV2RealTimeInfoId = 3018;
        public const string ImV2RealTimeInfoName = "ImV2RealTimeInfo";

        public const int ImV2SettingsId = 3019;
        public const string ImV2SettingsName = "ImV2Settings";

        public const int ImV2TotalStatsId = 3020;
        public const string ImV2TotalStatsName = "ImV2TotalStats";

        public const int ImV2CarTypeId = 3021;
        public const string ImV2CarTypeName = "ImV2CarType";

        public const int ImV2ModelUnknownId = 3022;
        public const string ImV2ModelUnknownName = "ImV2ModelUnknown";
    }
}
