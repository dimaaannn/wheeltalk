using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WheelTalk.Core.Contracts;
using WheelTalk.Core.Detection;
using WheelTalk.Core.Ports;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Ble;
using WheelTalk.Droid.Configuration;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Dashboard.Droid;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Ui;

using WheelTalk.Droid.App;

namespace WheelTalk.Droid.Scan;

/// <summary>
/// Поиск колеса: статус и список колёс. Логика скана уже нативная
/// (<see cref="AndroidBleClient"/>/<see cref="BleReadiness"/>) — портируется только экран, с эталона
/// <c>WheelTalk.App/Pages/ScanPage.xaml(.cs)</c> (опись §1.3, §5): «поиск отдаёт устройства, тап по
/// строке — подключение и возврат на главный экран».
/// <para>
/// Кнопки «Сканировать» нет: поиск начинается сам при появлении экрана и останавливается при уходе
/// с него. Сюда приходят ровно за одним — найти колесо, — и лишнее нажатие между «открыл экран» и
/// «увидел список» не давало ничего, кроме экрана, на котором ничего не происходит.
/// </para>
/// <para>
/// <b>Привязанные колёса стоят вверху</b> (план 24 §А), свежие первыми, и живут там независимо от
/// скана: подключиться можно и к тому, чего в эфире сейчас нет, — <c>ConnectAsync</c> начнёт
/// погоню, и она поймает колесо, когда его включат. Найденное привязанное показывается только
/// вверху, иначе оно задвоилось бы само с собой.
/// </para>
/// </summary>
[Activity]
public sealed class ScanActivity : Activity
{
    private AndroidBleClient _ble = null!;
    private WheelSession _session = null!;
    private WheelOptions _wheel = null!;
    private UserSettingsStore _settings = null!;
    private BoundWheels _bound = null!;
    private DashboardOptions _dashboard = null!;
    private ILogger<ScanActivity> _logger = null!;

    /// <summary>Что нашёл скан — <b>всё</b>, включая безымянное: привязанное колесо узнаётся по MAC, а не по имени.</summary>
    private readonly List<DiscoveredDevice> _devices = [];

    /// <summary>Привязанные колёса, как их отдало хранилище. Перечитывается при появлении экрана и после забывания.</summary>
    private IReadOnlyList<BoundWheel> _boundWheels = [];

    private readonly List<WheelRow> _rows = [];
    private WheelAdapter _adapter = null!;

    private TextView _statusLabel = null!;

    private CancellationTokenSource? _scanCts;
    private Task? _scanTask;

    /// <summary>
    /// Колесо, за которым сессия гонялась до прихода сюда. Погоня на время поиска останавливается —
    /// попытки подключения мешают скану, а иногда ломают его совсем, — и возобновляется при уходе с
    /// экрана. Пусто — гнаться было не за кем (первый запуск).
    /// </summary>
    private string? _chased;

    /// <summary>Выбрали новое колесо — старое возвращать не надо, к новому уже подключаются.</summary>
    private bool _switched;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Заголовок ставится кодом, а не атрибутом [Activity]: в атрибут можно положить только
        // константу, а строки живут в ресурсах (план 6, фаза 1 — вшитых строк не осталось).
        Title = string.Format(CultureInfo.CurrentCulture, AppStrings.ScreenTitleFormat, AppStrings.ScreenTitleScan);

        _ble = MainApplication.Services.GetRequiredService<AndroidBleClient>();
        _session = MainApplication.Services.GetRequiredService<WheelSession>();
        _wheel = MainApplication.Services.GetRequiredService<IOptions<WheelOptions>>().Value;
        _settings = MainApplication.Services.GetRequiredService<UserSettingsStore>();
        _bound = MainApplication.Services.GetRequiredService<BoundWheels>();
        _dashboard = MainApplication.Services.GetRequiredService<DashboardOptions>();
        _logger = MainApplication.Services.GetRequiredService<ILogger<ScanActivity>>();

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    protected override void OnStart()
    {
        base.OnStart();

        _statusLabel.SetText(AppStrings.ScanReady);

        // Запоминается до остановки: DisconnectAsync стирает адрес у сессии, и после него спросить
        // уже не у кого.
        _chased = _session.Address;

        ReloadBound();
        StartScan();
    }

    protected override void OnStop()
    {
        _ = StopScanAsync();
        ResumeChase();
        base.OnStop();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        BleReadiness.OnRequestPermissionsResult(requestCode, grantResults);
    }

    /// <summary>
    /// Запуск поиска при появлении экрана. Разрешения и выключенный Bluetooth спрашиваются здесь же
    /// и здесь же превращаются в строку состояния: раньше это делало нажатие, а теперь спросить
    /// некому — экран обязан сказать сам, почему списка нет.
    /// <para>
    /// Повторный вызов при уже идущем поиске ничего не делает: <c>OnStart</c> приходит и после
    /// возврата из диалога разрешений.
    /// </para>
    /// </summary>
    private async void StartScan()
    {
        try
        {
            if (_scanCts is not null) return;

            // Пока ищем — не подключаемся. Это единственный способ остановить погоню, и здесь он
            // уместен: пришедший сюда человек выбирает колесо заново, а попытки подключиться к
            // прежнему в это время только отбирают радио у скана.
            await _session.DisconnectAsync();

            if (!_ble.IsBluetoothEnabled)
            {
                _statusLabel.SetText(AppStrings.ScanBluetoothOff);
                return;
            }

            if (await BleReadiness.FindProblemAsync(this) is { } problem)
            {
                _statusLabel.SetText(problem.Message);
                return;
            }

            _scanTask = ScanUntilStopped();
            await _scanTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan.StartFailed");
            _statusLabel.SetText(AppStrings.ActionFailed);
        }
    }

    private async Task ScanUntilStopped()
    {
        _devices.Clear();
        Rebuild();
        _scanCts = new CancellationTokenSource();
        _statusLabel.SetText(AppStrings.ScanInProgress);

        try
        {
            // ScanAsync отдаёт устройства с колбэка на биндер-потоке — здесь, в отличие от MAUI, нет
            // синхронизационного контекста, который сам вернул бы продолжение в UI-поток, поэтому
            // каждое обращение к разметке явно завёрнуто в RunOnUiThread (план 12 §4).
            await foreach (var device in _ble.ScanAsync(_scanCts.Token))
            {
                var found = device;
                RunOnUiThread(() => Show(found));
            }
        }
        catch (System.OperationCanceledException)
        {
            // ожидаемо — нажали «Стоп» или ушли с экрана
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan.Failed");
            RunOnUiThread(() => _statusLabel.SetText(string.Format(AppStrings.ScanFailed, ex.Message)));
        }
    }

    /// <summary>Adds the device, or replaces its row when a later advertisement resolves the name.</summary>
    private void Show(DiscoveredDevice device)
    {
        int existing = _devices.FindIndex(d => d.Address == device.Address);
        if (existing < 0) _devices.Add(device);
        else _devices[existing] = device;

        Rebuild();

        // Считаются показанные строки, а не всё, что ответило: безымянная мелочь из соседних
        // квартир в список не попадает и обещать её человеку незачем.
        _statusLabel.SetText(string.Format(
            CultureInfo.CurrentCulture, AppStrings.ScanFound, _rows.Count(r => r.Found)));
    }

    private void ReloadBound()
    {
        _boundWheels = _bound.All();
        Rebuild();
    }

    /// <summary>
    /// Список целиком: привязанные сверху (свежие первыми — порядок задаёт хранилище), под ними
    /// найденное новое. Привязанное, найденное сканом, остаётся только вверху: сверка по MAC, иначе
    /// одно колесо стояло бы в списке дважды.
    /// <para>
    /// Строится заново на каждую находку и не пользуется DiffUtil — список короткий и живёт только
    /// пока открыт экран, ровно как и раньше.
    /// </para>
    /// </summary>
    private void Rebuild()
    {
        _rows.Clear();

        foreach (var wheel in _boundWheels)
        {
            var advertised = Advertisement(wheel.Mac);

            // Алиас, данный человеком; нет его — имя анонса, но только пока колесо в эфире (это
            // живые данные, расходиться им не с чем); нет и его — голый MAC.
            string caption = wheel.Alias.Length > 0 ? wheel.Alias
                : advertised?.Name is { Length: > 0 } name ? name
                : wheel.Mac;

            // Словом, а не одним цветом: зелёное от серого отличает не всякий глаз, и палитра по
            // умолчанию выбрана как раз из этих соображений.
            string state = advertised is null ? AppStrings.ScanWheelSilent : AppStrings.ScanWheelInAir;

            _rows.Add(new WheelRow(
                wheel.Mac, caption, Bound: true, Found: advertised is not null,
                Details: caption == wheel.Mac ? state : $"{wheel.Mac}   {state}"));
        }

        foreach (var device in _devices)
        {
            // Безымянное показывать нечем: MAC уже стоит второй строкой, а первой встал бы он же.
            if (device.Name.Length == 0 || _boundWheels.Any(w => Same(w.Mac, device.Address))) continue;

            _rows.Add(new WheelRow(device.Address, device.Name, Bound: false, Found: true,
                Details: $"{device.Address}   {device.Rssi} dBm"));
        }

        _adapter.NotifyDataSetChanged();
    }

    private DiscoveredDevice? Advertisement(string mac) => _devices.Find(d => Same(d.Address, mac));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Stops the scan and waits for the loop to unwind — a scan still in flight would keep
    /// writing over the status text while the connection is being set up.</summary>
    private async Task StopScanAsync()
    {
        _scanCts?.Cancel();

        if (_scanTask is not null)
        {
            await _scanTask;
            _scanTask = null;
        }

        _scanCts?.Dispose();
        _scanCts = null;
    }

    /// <summary>
    /// Уходим с экрана — возвращаем погоню за прежним колесом. Ушли, ничего не выбрав (кнопка
    /// «назад», погас экран), — связь должна восстанавливаться сама, как будто сюда и не заходили:
    /// иначе заглянувший в поиск райдер остался бы и без нового колеса, и без старого.
    /// <para>
    /// Выбрали новое — не трогаем: к нему уже подключаются, и своим `ConnectAsync` мы бы это
    /// подключение оборвали.
    /// </para>
    /// </summary>
    private void ResumeChase()
    {
        if (_switched || _chased is null) return;

        string address = _chased;
        _chased = null;

        _ = Task.Run(async () =>
        {
            try
            {
                await _session.ConnectAsync(address);
            }
            catch (Exception ex)
            {
                // Сессия продолжит гоняться сама — здесь только первая неудача, и она не новость.
                _logger.LogError(ex, "Scan.ResumeFailed {Mac}", address);
            }
        });
    }

    /// <summary>
    /// Тап по строке — обычное подключение, и для привязанного колеса оно ровно то же самое:
    /// <c>ConnectAsync</c> возвращается после первой попытки, а погоня продолжается фоном. Поэтому
    /// серая строка кликабельна, а экран после неё закрывается на главный, как при любом выборе:
    /// «ищем», а не «не удалось».
    /// </summary>
    private async void OnWheelSelected(WheelRow row)
    {
        try
        {
            // Скан во время подключения его замедляет, а иногда мешает совсем.
            await StopScanAsync();

            _statusLabel.SetText(string.Format(CultureInfo.CurrentCulture, AppStrings.ScanConnecting, row.Caption));

            try
            {
                await _session.ConnectAsync(row.Address);
            }
            catch (Exception ex) when (ex is WheelNotRecognisedException or WheelNotSupportedException)
            {
                // Не наше колесо. Причина остаётся на экране поиска — человек здесь и выбирает,
                // так что показать её надо тут же, рядом со списком, а не диалогом поверх.
                _logger.LogWarning(ex, "Connect.Refused {Mac}", row.Address);
                _statusLabel.SetText(ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connect.Failed {Mac}", row.Address);
                _statusLabel.SetText(string.Format(CultureInfo.CurrentCulture, AppStrings.ScanConnectFailed, ex.Message));
                return;
            }

            // Дальше уходить с экрана можно, не возвращая прежнее колесо: за этим уже гонятся.
            _switched = true;

            // Тот же вызов, что в MainActivity.Connect: связь установлена — процесс обязан пережить
            // карман. Без него подключение из поиска жило без foreground-сервиса: уведомления в
            // шторке нет, Android замораживает процесс с погашенным экраном, и погоня после обрыва
            // стоит, пока экран не включат (найдено полевым выходом 31.07.2026 — «отключено 900 с»).
            WheelForegroundService.Start();

            // Колесо, выбранное вручную, становится тем, к которому приложение подключается само.
            // Протокол не сохраняется: его больше не выбирают, а опознают на каждом подключении.
            _settings.SaveWheel(row.Address);

            // Назад на главный экран: он подхватит уже идущую сессию, когда снова станет видимым.
            Finish();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ui.DeviceSelectFailed {Mac}", row.Address);
            _statusLabel.SetText(AppStrings.ActionFailed);
        }
    }

    /// <summary>
    /// Долгий тап по привязанному — «забыть все настройки» (план 24 §А3). Уносит разом имя, пароль и
    /// пороги этого колеса, поэтому спрашивается подтверждение; поездки остаются, и вопрос об этом
    /// говорит прямо. По новому колесу забывать нечего — жест там ничего не делает.
    /// </summary>
    private async void OnWheelLongPressed(WheelRow row)
    {
        try
        {
            if (!row.Bound) return;

            bool confirmed = await ConfirmAsync(
                AppStrings.ScanForget,
                string.Format(CultureInfo.CurrentCulture, AppStrings.ScanForgetConfirm, row.Caption));

            if (!confirmed) return;

            await _bound.ForgetAsync(row.Address);

            // Забытое колесо возвращать некуда: без этого уход с экрана воскресил бы его погоней за
            // прежним (ResumeChase), и забывание отменилось бы само.
            if (_chased is not null && Same(_chased, row.Address)) _chased = null;

            ReloadBound();
            _statusLabel.SetText(string.Format(CultureInfo.CurrentCulture, AppStrings.ScanForgotten, row.Caption));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ui.WheelForgetFailed {Mac}", row.Address);
            _statusLabel.SetText(AppStrings.ActionFailed);
        }
    }

    private Task<bool> ConfirmAsync(string title, string message)
    {
        var answer = new TaskCompletionSource<bool>();
        new AlertDialog.Builder(this)!
            .SetTitle(title)!
            .SetMessage(message)!
            .SetCancelable(false)!
            .SetPositiveButton(AppStrings.ScanForget, (_, _) => answer.TrySetResult(true))!
            .SetNegativeButton(AppStrings.Cancel, (_, _) => answer.TrySetResult(false))!
            .Show();
        return answer.Task;
    }

    // ---- Разметка ---------------------------------------------------------------------------

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetBackgroundColor(this.PageBackground());
        int pad = this.Dp(16);
        root.SetPadding(pad, pad, pad, pad);

        // Переключателя протокола здесь больше нет: колесо опознаётся само — по дереву GATT при
        // подключении и по заголовку первого кадра. Выбор руками был единственным местом, где
        // человек мог ошибиться и получить молчащее колесо вместо работающего.
        _statusLabel = new TextView(this) { Text = AppStrings.ScanReady };
        _statusLabel.SetTextSize(ComplexUnitType.Sp, 14);
        _statusLabel.SetTextColor(UiKit.PlainText(this));
        root.AddView(_statusLabel, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = this.Dp(12) });

        var list = new RecyclerView(this) { LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f) { TopMargin = this.Dp(12) } };
        list.SetLayoutManager(new LinearLayoutManager(this));
        _adapter = new WheelAdapter(this, _rows, _dashboard, OnWheelSelected, OnWheelLongPressed);
        list.SetAdapter(_adapter);
        root.AddView(list);

        return root;
    }

    /// <summary>
    /// Строка списка — подпись колеса и адрес с состоянием под ней. Без DiffUtil: список короткий и
    /// живёт только пока открыт экран.
    /// <para>
    /// Цвет берётся из палитры, а не из константы: привязанное и найденное — «хорошо» (у палитры
    /// Ванга это сине-зелёный, различимый при дейтеранопии), привязанное и молчащее — «приглушённое».
    /// Новое колесо остаётся обычным текстом: зелёный у него значил бы то же, что и у привязанного,
    /// а это разные вещи.
    /// </para>
    /// </summary>
    private sealed class WheelAdapter(
        Context context,
        List<WheelRow> rows,
        DashboardOptions dashboard,
        Action<WheelRow> onSelected,
        Action<WheelRow> onLongPress)
        : RecyclerView.Adapter
    {
        public override int ItemCount => rows.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var layout = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Vertical };
            int padV = context.Dp(10);
            layout.SetPadding(0, padV, 0, padV);

            var name = new TextView(context) { Id = View.GenerateViewId() };
            name.SetTextSize(ComplexUnitType.Sp, 17);
            layout.AddView(name);

            var details = new TextView(context) { Id = View.GenerateViewId() };
            details.SetTextSize(ComplexUnitType.Sp, 12);
            details.Alpha = 0.7f;
            layout.AddView(details);

            return new Holder(layout, name, details);
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
        {
            var row = rows[position];
            var h = (Holder)holder;
            h.Name.SetText(row.Caption);
            h.Name.SetTextColor(row.Bound
                ? row.Found ? dashboard.Palette.Good : dashboard.Palette.Dim
                : UiKit.PlainText(context));
            h.Details.SetText(row.Details);

            h.ItemView.Click -= h.ClickHandler;
            h.ClickHandler = (_, _) => onSelected(row);
            h.ItemView.Click += h.ClickHandler;

            h.ItemView.LongClick -= h.LongClickHandler;
            h.LongClickHandler = (_, e) => { onLongPress(row); e.Handled = true; };
            h.ItemView.LongClick += h.LongClickHandler;
        }

        private sealed class Holder(View itemView, TextView name, TextView details) : RecyclerView.ViewHolder(itemView)
        {
            public TextView Name => name;
            public TextView Details => details;
            public EventHandler? ClickHandler;
            public EventHandler<View.LongClickEventArgs>? LongClickHandler;
        }
    }

    /// <summary>
    /// Строка списка, одна на оба вида колёс: привязанное и просто найденное. Одна модель, а не две,
    /// потому что тап по ним делает одно и то же — подключается по адресу.
    /// </summary>
    private sealed record WheelRow(string Address, string Caption, bool Bound, bool Found, string Details);
}
