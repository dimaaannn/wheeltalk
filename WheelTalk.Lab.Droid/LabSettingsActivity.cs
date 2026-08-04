using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Dashboard.Droid;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// Все ручки одним экраном, длинным списком по группам. Собран кодом из одинаковых строк нарочно:
/// ручек четыре десятка, и в разметке они превратились бы в полотно, где добавить ещё одну —
/// работа, а не строка.
/// <para>
/// Порядок групп — от того, что меняют чаще, к тому, что трогают раз: сначала шкалы и пороги,
/// потом приёмы, потом оформление, и в самом конце правка записи, которая к панели отношения не
/// имеет и нужна только стенду.
/// </para>
/// <para>
/// Портировано с <c>WheelTalk.Lab/Pages/LabSettingsPage.cs</c>: <c>Slider</c> → <see cref="SeekBar"/>
/// (у того целочисленная шкала, поэтому значение раскладывается по тысяче делений),
/// <c>Switch</c> → <see cref="Android.Widget.Switch"/>, <c>Picker</c> → <see cref="Spinner"/>.
/// Состав ручек, их пределы и подписи — те же, кроме одной: «снять потолок в 30 кадров» ушла
/// вместе с таймером кадров. Панель рисует себя по vsync, потолка, который можно снять, больше нет.
/// </para>
/// <para>
/// Настройки живут только пока приложение запущено — это стенд, а не приложение.
/// </para>
/// </summary>
[Activity(Label = "Параметры")]
public sealed class LabSettingsActivity : Activity
{
    /// <summary>Во сколько делений раскладывается любой диапазон: у SeekBar шкала целая.</summary>
    private const int Steps = 1000;

    private readonly LabSettings _settings = LabSettings.Current;

    private LinearLayout _rows = null!;
    private float _density;

    /// <summary>
    /// Escape закрывает параметры — тем же движением, каким на стенде им зовут хром. Одна клавиша
    /// значит «покажи стенд»: там, где он спрятан, она его достаёт, здесь — возвращает к нему.
    /// </summary>
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is { KeyCode: Keycode.Escape, Action: KeyEventActions.Down })
        {
            Finish();
            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _density = Resources!.DisplayMetrics!.Density;

        _rows = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _rows.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));

        var scroller = new ScrollView(this);
        scroller.SetBackgroundColor(Color.ParseColor("#101010"));
        scroller.AddView(_rows);
        SetContentView(scroller);

        var options = _settings.Options;

        Group("Шкала ШИМ");
        Note("Лента справа. Шкала бесконечна в обе стороны: концы — не границы, а запас.");
        Slider("Серая ниже, %", 0, 90, options.PwmGreyBelow, "F0", v => options.PwmGreyBelow = v);
        Slider("Плотность, dp на процент", 6, 20, options.PwmDpPerUnit, "F0", v => options.PwmDpPerUnit = v);

        Group("Пороги ШИМ");
        Note("Границы цветных зон и трёх ступеней сигнала в окне значения.");
        // Стенд не подменяет Thresholds ничем — умолчание DashboardOptions уже DashboardThresholds,
        // мутабельная реализация ровно для этого (план 19 Б3).
        var thresholds = (DashboardThresholds)options.Thresholds;
        Slider("Внимание (жёлтая зона), %", 60, 95, thresholds.WarnPwm, "F0", v => thresholds.WarnPwm = v);
        Slider("Опасно (красный фон), %", 70, 105, thresholds.DangerPwm, "F0", v => thresholds.DangerPwm = v);
        Slider("Критично (мигание, штриховка), %", 80, 115, options.BarberPolePwm, "F0", v => options.BarberPolePwm = v);

        Group("Шкала напряжения");
        Note("Лента слева. Пороги абсолютные: зона стоит на шкале неподвижно. Ноль выключает зону.");
        Slider("Видно на ленте, В", 4, 40, options.SagWindowVolts, "F0", v => options.SagWindowVolts = v);
        Toggle("Растягивать под размах поездки", options.SagAutoScale, v => options.SagAutoScale = v);
        Slider("Жёлтая ниже, В", 0, 250, options.WarnVolts, "F1", v => options.WarnVolts = v);
        Slider("Красная ниже, В", 0, 250, options.DangerVolts, "F1", v => options.DangerVolts = v);
        Slider("Пак пуст ниже, В (0 — выкл.)", 0, 250, options.EmptyVolts, "F0", v => options.EmptyVolts = v);

        Group("Приёмы");
        Note("Что рисуется поверх шкал. Выключается по одному, чтобы было видно, что даёт каждое.");
        Toggle("Стрелка тренда и просадки", options.ShowTrend, v => options.ShowTrend = v);
        Slider("Прогноз на, с", 0.5, 5, options.TrendSeconds, "F1", v => options.TrendSeconds = v);
        Toggle("Следы поездки (метки максимума и минимума)", options.ShowBug, v => options.ShowBug = v);
        Toggle("Штриховка выше критического", options.ShowBarberPole, v => options.ShowBarberPole = v);

        Group("Цифры");
        Slider("Скрывать десятые выше, км/ч", 0, 60, options.HideTenthsAbove, "F0", v => options.HideTenthsAbove = v);
        Slider("Убирать справочные выше, км/ч", 0, 60, options.HideExtrasAbove, "F0", v => options.HideExtrasAbove = v);
        Note("Ноль в любом из двух выключает правило совсем.");

        Group("Движение");
        Slider("Сглаживание хода лент, с", 0, 0.3, options.TapeSmoothSeconds, "F2", v => options.TapeSmoothSeconds = v);
        Slider("Сглаживание данных ШИМ, с", 0, 1, options.SmoothingSeconds, "F2", v => options.SmoothingSeconds = v);
        Note("Первое сглаживает только разметку, цифра остаётся сырой. Второе фильтрует сами данные и задерживает всё, включая пик.");

        Group("Тревога");
        Toggle("Показывать полосы тревоги", options.ShowAlertBorder, v => options.ShowAlertBorder = v);
        Slider("Частота моргания полос, Гц", 1, 6, options.BlinkHz, "F1", v => options.BlinkHz = v);

        Group("Хром панели");
        Note("Всё это рисует сама панель своей канвой: разметке они не стоят ни одной точки, а появляются и исчезают, ничего не двигая.");
        Slider("Скрывать имя колеса выше, км/ч", 0, 30, options.ShowNameBelow, "F0", v => options.ShowNameBelow = v);
        Toggle("Идёт запись (точка в углу)", _settings.Recording, v => _settings.Recording = v);
        Toggle("Подсказка про шторку", _settings.ShowSheetHint, v => _settings.ShowSheetHint = v);
        Toggle("Вуаль устаревших данных", _settings.Stale, v => _settings.Stale = v);
        Toggle("Панель под системной строкой", _settings.UnderSystemBar, v => _settings.UnderSystemBar = v);
        Toggle("Тревога колеса (режим «экран целиком»)", _settings.WheelAlarm, v => _settings.WheelAlarm = v);

        Group("Оформление");
        Slider("Ширина ленты, % экрана", 20, 40, options.TapeShare, "F0", v => options.TapeShare = v);
        Note("Замечание прогона 4 — ленты шире. Платит за это центр: кегль скорости подбирается под его ширину.");
        Slider("Наклон панели, °", 0, 45, options.Tilt, "F0", v => options.Tilt = v);
        Palette();

        Group("Старые варианты (B…G)");
        Note("Эти ручки вариант A не трогают — они остались от вариантов, которые живут как история.");
        Slider("Начало шкалы (дуга, сегменты), %", 0, 90, options.ScaleMin, "F0", v => options.ScaleMin = v);
        Slider("Конец шкалы (дуга, сегменты), %", 90, 120, options.ScaleMax, "F0", v => options.ScaleMax = v);
        Slider("Шаг сегмента, %", 1, 10, options.SegmentPercent, "F0", v => options.SegmentPercent = v);
        Slider("Личный предел (бирка), %", 0, 105, options.PersonalLimit, "F0", v => options.PersonalLimit = v);
        Slider("Кольцо заполнено при, км/ч", 20, 120, options.ReferenceSpeed, "F0", v => options.ReferenceSpeed = v);
        Toggle("ШИМ вытесняет скорость", options.PwmCrowdsOutSpeed, v => options.PwmCrowdsOutSpeed = v);

        Group("Правка записи (только стенд)");
        Note("К панели отношения не имеет: показывает поведение, которого в записи нет.");
        Slider("ШИМ ×", 0.5, 2.5, _settings.Tweaks.PwmGain, "F2",
            v => _settings.Tweaks = _settings.Tweaks with { PwmGain = v }, applyOnRelease: true);
        Slider("Скорость ×", 0.5, 3, _settings.Tweaks.SpeedGain, "F2",
            v => _settings.Tweaks = _settings.Tweaks with { SpeedGain = v }, applyOnRelease: true);
        Slider("Время ×", 0.25, 4, _settings.Tweaks.TimeScale, "F2",
            v => _settings.Tweaks = _settings.Tweaks with { TimeScale = v }, applyOnRelease: true);
    }

    private void Group(string title)
    {
        var label = new TextView(this) { Text = title.ToUpperInvariant() };
        label.SetTextSize(ComplexUnitType.Sp, 12);
        label.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        label.Alpha = 0.6f;
        Add(label, topMargin: 18, bottomMargin: 4);
    }

    /// <summary>Строка-пояснение под заголовком группы: что эта группа вообще меняет.</summary>
    private void Note(string text)
    {
        var label = new TextView(this) { Text = text };
        label.SetTextSize(ComplexUnitType.Sp, 12);
        label.Alpha = 0.45f;
        Add(label, bottomMargin: 6);
    }

    /// <param name="applyOnRelease">
    /// Для правок записи: они пересобирают весь сценарий, и делать это на каждом движении пальца
    /// значит елозить ползунком по застывшему экрану.
    /// </param>
    private void Slider(string title, double min, double max, double value, string format, Action<double> apply,
        bool applyOnRelease = false)
    {
        var caption = new TextView(this);
        caption.SetTextSize(ComplexUnitType.Sp, 14);

        void Show(double current) => caption.Text = $"{title}: {current.ToString(format)}";
        Show(value);

        double Value(int progress) => min + (max - min) * progress / Steps;

        var slider = new SeekBar(this)
        {
            Max = Steps,
            Progress = (int)Math.Round((Math.Clamp(value, min, max) - min) / (max - min) * Steps),
        };
        slider.ProgressChanged += (_, e) =>
        {
            Show(Value(e.Progress));
            if (applyOnRelease || !e.FromUser) return;
            apply(Value(e.Progress));
            _settings.Notify();
        };
        if (applyOnRelease)
        {
            slider.StopTrackingTouch += (_, _) =>
            {
                apply(Value(slider.Progress));
                _settings.Notify();
            };
        }

        Add(caption);
        Add(slider);
    }

    private void Toggle(string title, bool value, Action<bool> apply)
    {
        var toggle = new Switch(this) { Checked = value };
        toggle.CheckedChange += (_, e) =>
        {
            apply(e.IsChecked);
            _settings.Notify();
        };

        var label = new TextView(this) { Text = title };
        label.SetTextSize(ComplexUnitType.Sp, 14);

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.AddView(label, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        row.AddView(toggle);

        Add(row);
    }

    private void Palette()
    {
        var spinner = new Spinner(this);
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
            DashboardPalette.All.Select(p => p.Name).ToArray());
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spinner.Adapter = adapter;
        spinner.SetSelection(DashboardPalette.All.ToList().IndexOf(_settings.Options.Palette));
        spinner.ItemSelected += (_, e) =>
        {
            var palette = DashboardPalette.All[e.Position];
            if (palette == _settings.Options.Palette) return;
            _settings.Options.Palette = palette;
            _settings.Notify();
        };

        Add(spinner);
    }

    private void Add(View view, int topMargin = 0, int bottomMargin = 2)
    {
        _rows.AddView(view, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(topMargin),
            BottomMargin = Dp(bottomMargin),
        });
    }

    private int Dp(float dp) => (int)Math.Round(dp * _density);
}
