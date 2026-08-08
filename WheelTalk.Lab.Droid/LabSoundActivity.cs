using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using WheelTalk.Lab.Droid.Sound;

namespace WheelTalk.Lab.Droid;

/// <summary>
/// Страница звука тревоги: восемь вариантов, слушать подряд, запомнить выбранный (план 26).
/// <para>
/// <b>Собрана под улицу, а не под стол.</b> Отсюда всё её устройство: две кнопки в треть экрана —
/// «играть» и «следующий», потому что попасть в них надо на ходу и в перчатке; имя варианта крупно,
/// потому что читается оно мельком; выбор пишется в файл, потому что стенд к разбору дома может уже
/// не жить. Список вариантов внизу — для стола, на ходу к нему не тянутся.
/// </para>
/// <para>
/// Громкость вариантов выровнена проигрывателем: сравнивают приёмы, а не уровни. Клавиши громкости
/// правят поток тревоги — тот же, в котором звучит боевой сигнал, — чтобы не оказалось, что на
/// выезде крутили медиа.
/// </para>
/// <para>
/// <b>Два отобранных варианта уже стоят в приложении</b> (план 26) и берутся здесь из ядра, а не
/// повторены: страница слушает ровно то, чем звучит бой. Остальные варианты — опытные, они живут
/// только тут.
/// </para>
/// </summary>
// Имя задано явно — по той же причине, что у LabActivity: командный вход не должен зависеть от
// crc64-хеша пространства имён.
[Activity(Name = "com.wheeltalk.lab.droid.LabSoundActivity", Label = "Звук тревоги",
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode)]
public sealed class LabSoundActivity : Activity
{
    private const int Steps = 100;

    private readonly AlarmVoicePlayer _player = new();

    private AlarmVoice _voice = AlarmVoices.All[0];
    private AlarmVoice? _chosen;

    private float _density;
    private TextView _title = null!;
    private TextView _note = null!;
    private TextView _chosenLabel = null!;
    private Button _playButton = null!;
    private readonly Dictionary<string, Button> _rows = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _density = Resources!.DisplayMetrics!.Density;

        // Клавиши громкости — потоку тревоги: подбирать уровень будут ими, и подобрать надо тот
        // поток, в котором сигнал звучит на самом деле.
        VolumeControlStream = Android.Media.Stream.Alarm;

        _chosen = LabVoiceChoice.Load();
        _voice = _chosen ?? AlarmVoices.All[0];
        _player.Voice = _voice;

        SetContentView(BuildLayout());
        ShowVoice();

        HandleCommand(Intent);
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Экран не гаснет: страницу держат открытой весь заезд, а гашение уводит стенд в фон, где
        // EMUI его и убивает вместе со звуком.
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
    }

    protected override void OnStop()
    {
        Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
        base.OnStop();
    }

    /// <summary>
    /// Звук держится, пока страница открыта, и умирает вместе с ней: телефон едет в кармане, экран
    /// при этом может и погаснуть, и остановка по <c>OnPause</c> обрывала бы ровно тот опыт, ради
    /// которого страница написана.
    /// </summary>
    protected override void OnDestroy()
    {
        _player.Dispose();
        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleCommand(intent);
    }

    /// <summary>
    /// Командный вход, тот же приём, что у стенда:
    /// <c>am start -n com.wheeltalk.lab.droid/.LabSoundActivity --es voice sweep --es play on</c>.
    /// Нужен затем же: попадание пальцем по координатам промахивается, а прогон терять жалко.
    /// </summary>
    private void HandleCommand(Intent? intent)
    {
        if (intent is null) return;

        if (intent.GetStringExtra("voice") is { } id) Select(AlarmVoices.ById(id), play: false);
        if (intent.GetStringExtra("play") is { } play) SetPlaying(play != "off");
    }

    private void Select(AlarmVoice voice, bool play)
    {
        _voice = voice;
        _player.Voice = voice;
        ShowVoice();
        if (play) SetPlaying(true);
    }

    private void Next(int step)
    {
        int index = AlarmVoices.All.ToList().IndexOf(_voice);
        int count = AlarmVoices.All.Count;
        Select(AlarmVoices.All[((index + step) % count + count) % count], play: _player.IsPlaying);
    }

    private void SetPlaying(bool playing)
    {
        if (playing) _player.Play(); else _player.Stop();
        _playButton.Text = playing ? "⏹  Стоп" : "▶  Играть";
    }

    private void Remember()
    {
        _chosen = _voice;
        LabVoiceChoice.Save(_voice);
        ShowVoice();
    }

    private void ShowVoice()
    {
        _title.Text = _voice.Title;
        _note.Text = _voice.Note;
        _chosenLabel.Text = _chosen is null ? "Выбор не сделан" : $"Выбран: {_chosen.Title}";

        foreach (var voice in AlarmVoices.All)
        {
            var row = _rows[voice.Id];
            row.Text = (voice.Id == _chosen?.Id ? "★  " : "     ") + voice.Title;
            row.SetTextColor(voice.Id == _voice.Id ? Color.White : Color.ParseColor("#9E9E9E"));
        }
    }

    // ---- Разметка, собранная кодом (как весь остальной стенд) ---------------------------------

    private View BuildLayout()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.ParseColor("#101010"));
        root.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));

        _title = new TextView(this);
        _title.SetTextSize(ComplexUnitType.Sp, 24);
        _title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        _title.SetTextColor(Color.White);
        root.AddView(_title, Row());

        _note = new TextView(this);
        _note.SetTextSize(ComplexUnitType.Sp, 13);
        _note.Alpha = 0.6f;
        root.AddView(_note, Row(bottom: 10));

        root.AddView(BuildIntensity(), Row());
        root.AddView(BuildControls(), Row(top: 6));

        var remember = BigButton("★  Запомнить этот", Remember);
        root.AddView(remember, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(56)) { TopMargin = Dp(8) });

        _chosenLabel = new TextView(this);
        _chosenLabel.SetTextSize(ComplexUnitType.Sp, 13);
        _chosenLabel.Alpha = 0.7f;
        root.AddView(_chosenLabel, Row(top: 6, bottom: 6));

        var list = new LinearLayout(this) { Orientation = Orientation.Vertical };
        foreach (var voice in AlarmVoices.All)
        {
            var target = voice;
            var row = BigButton(voice.Title, () => Select(target, play: _player.IsPlaying));
            row.Gravity = GravityFlags.CenterVertical | GravityFlags.Start;
            row.SetTextSize(ComplexUnitType.Sp, 15);
            _rows[voice.Id] = row;
            list.AddView(row, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, Dp(44)));
        }

        var scroller = new ScrollView(this);
        scroller.AddView(list);
        root.AddView(scroller, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        return root;
    }

    /// <summary>
    /// Интенсивность правит рисунок — длину писка и паузу между пачками, — а не громкость. Слушать
    /// надо оба конца: у порога и на потолке это разные звуки, и пропасть они могут по-разному.
    /// </summary>
    private View BuildIntensity()
    {
        var caption = new TextView(this);
        caption.SetTextSize(ComplexUnitType.Sp, 14);

        var slider = new SeekBar(this) { Max = Steps, Progress = Steps };
        void Show(int progress) => caption.Text = $"Интенсивность (рисунок, не громкость): {progress / 100.0:F2}";
        Show(slider.Progress);

        slider.ProgressChanged += (_, e) =>
        {
            Show(e.Progress);
            _player.Intensity = e.Progress / (double)Steps;
        };
        _player.Intensity = 1;

        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.AddView(caption, Row());
        box.AddView(slider, Row());
        return box;
    }

    /// <summary>
    /// Две кнопки в треть экрана: по ним попадают на ходу, не глядя. Всё остальное на этой странице
    /// — для стола.
    /// </summary>
    private View BuildControls()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };

        _playButton = BigButton("▶  Играть", () => SetPlaying(!_player.IsPlaying));
        row.AddView(_playButton, new LinearLayout.LayoutParams(0, Dp(96), 1f) { RightMargin = Dp(6) });

        row.AddView(BigButton("Следующий  ▶", () => Next(1)),
            new LinearLayout.LayoutParams(0, Dp(96), 1f));

        var back = BigButton("◀  Предыдущий", () => Next(-1));
        back.SetTextSize(ComplexUnitType.Sp, 14);
        row.AddView(back, new LinearLayout.LayoutParams(Dp(120), Dp(96)) { LeftMargin = Dp(6) });

        return row;
    }

    private Button BigButton(string text, Action onClick)
    {
        var button = new Button(this) { Text = text };
        button.SetTextSize(ComplexUnitType.Sp, 18);
        button.SetAllCaps(false);
        button.Click += (_, _) => onClick();
        return button;
    }

    private LinearLayout.LayoutParams Row(int top = 0, int bottom = 2) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(top),
            BottomMargin = Dp(bottom),
        };

    private int Dp(float dp) => (int)Math.Round(dp * _density);
}
