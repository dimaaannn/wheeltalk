## Как проверять изменения в декодере

Ядро не зависит от BLE — декодер гоняется напрямую: hex-кадры из соответствующего `*AdapterTest.kt`
(`Wheellog.Android/app/src/test/java/com/cooper/wheellog/utils/`) → `Decoder.Feed` →
`WheelState.ToSnapshot()`. Все такие сверки закреплены постоянными тестами в `WheelTalk.Tests/`
(xUnit, `dotnet test`), одноразовый scratch-проект не нужен:

- `Decoding/{Gotway,Veteran,Kingsong,InMotion,InMotionV2}DecoderTests.cs` — перенесённые фикстуры
  оригинала. У Veteran это Sherman L, Abrams, Patton CRC, Lynx CRC, Oryx pnum=8; у Gotway —
  "2020 board data", "new board data" (×2, второй проход проверяет гейтинг
  true-voltage/true-current), "strange board data" и хендшейк NAME/GW. Все совпали 1:1.
- `Decoding/RealFrameDecodingTests.cs` / `RealInMotionFrameDecodingTests.cs` — физическая
  разумность значений на сырых BLE-дампах: Sherman L и три дампа оригинала InMotion V1.
- `TestSupport/DecoderHarness.cs` собирает `WheelState`/`IWheelDecoder`/`Decoder` так же, как
  `Program.Build()`, минус BLE. Скорость сверять только через
  `TestSupport/SnapshotAssertions.RoundedSpeed()` — почему, см. грабли в «Пять протоколов».
- `RecordedTelemetryValidationTests.cs` — sanity-проверки на реальной записи с MTen3
  (`TestHarness.RecordTelemetryCsv`, `Fixtures/mten3_recorded_20260719.csv`): диапазон напряжения
  для 84В/20S-пака, battery 0-100, WheelType, разрешение хендшейка модели/версии. Это не
  byte-in/value-out фикстура (сырых BLE-кадров в ней нет) — валидирует правдоподобность результата.

