using System.Globalization;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using WheelTalk.Core.Diagnostics;
using WheelTalk.Dashboard.Droid.Screen;
using WheelTalk.Droid.Resources.Strings;
using WheelTalk.Droid.Ui;

namespace WheelTalk.Droid.Diagnostics;

/// <summary>
/// Экран состава: что именно уйдёт, сколько это весит и чем открыть, — <b>до</b> системного диалога
/// «поделиться» (план 11 §4.4).
/// <para>
/// Это не бюрократия, а единственное, что отличает «поделиться журналом» от «отправить неизвестно
/// что»: в журнале MAC колеса, пути файлов и модель телефона. Под раздачу это ещё и позиция, при
/// которой не нужна политика конфиденциальности — данные не уходят никуда без явного действия
/// человека, и он видел, что уходит.
/// </para>
/// <para>
/// <b>Экраном, а не диалогом поверх настроек.</b> Кнопку жмут из каталога настроек, у которого на
/// руках нет живой активности (<see cref="DiagnosticsShare.Send"/> статична и зовётся из
/// описания настройки), а окно без хозяина — то, что течёт при смерти экрана. У активности хозяин
/// свой, системный.
/// </para>
/// </summary>
[Activity(Label = "@string/app_name")]
public sealed class DiagnosticsShareActivity : Activity
{
    private const string Authority = "com.wheeltalk.droid.fileprovider";

    /// <summary>
    /// Признак «полный журнал» в намерении: тот же экран, другой состав (решение владельца
    /// 15.08.2026). Второй активности заводить незачем — вопрос у экрана один и тот же, «что уйдёт»,
    /// и вторая копия разошлась бы с первой первой же правкой.
    /// </summary>
    public const string ExtraFullLog = "full-log";

    private IReadOnlyList<DiagnosticsPart> _parts = [];

    private bool _fullLog;

    /// <summary>
    /// Готовый архив полного режима. Собирается <b>при открытии</b>, а не по «Отправить»: владелец
    /// просил показать, сколько будет передано, — а это размер уже сжатого архива, и другого способа
    /// его узнать, кроме как собрать, нет.
    /// </summary>
    private string _archive = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _fullLog = Intent?.GetBooleanExtra(ExtraFullLog, false) ?? false;

        Title = _fullLog ? AppStrings.DiagnosticsFullTitle : AppStrings.DiagnosticsBundleTitle;
        _parts = _fullLog ? DiagnosticsBundle.PrepareFullLog() : DiagnosticsBundle.Prepare();
        if (_fullLog && _parts.Count > 0) _archive = DiagnosticsBundle.PackFullLog(_parts);

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    private View BuildLayout()
    {
        var scroll = new ScrollView(this);
        scroll.SetBackgroundColor(this.Surface());

        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int pad = this.Dp(16);
        root.SetPadding(pad, this.Dp(20), pad, pad);

        var title = new TextView(this)
        {
            Text = _fullLog ? AppStrings.DiagnosticsFullTitle : AppStrings.DiagnosticsBundleTitle,
        };
        title.SetTextSize(ComplexUnitType.Sp, 24);
        title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        title.SetTextColor(UiKit.PlainText(this));
        root.AddView(title);

        if (_parts.Count == 0)
        {
            var empty = new TextView(this) { Text = AppStrings.DiagnosticsBundleEmpty };
            empty.SetTextSize(ComplexUnitType.Sp, 15);
            empty.Alpha = 0.75f;
            root.AddView(empty, Below(this.Dp(20)));
        }

        foreach (var part in _parts) root.AddView(PartCard(part), Below(this.Dp(12)));

        var total = new TextView(this) { Text = Total() };
        total.SetTextSize(ComplexUnitType.Sp, 15);
        total.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        total.SetTextColor(UiKit.PlainText(this));
        root.AddView(total, Below(this.Dp(16)));

        root.AddView(Buttons(), Below(this.Dp(24)));

        scroll.AddView(root);
        return scroll;
    }

    private View PartCard(DiagnosticsPart part)
    {
        var card = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        card.SetGravity(GravityFlags.CenterVertical);
        card.SetPadding(this.Dp(14), this.Dp(12), this.Dp(14), this.Dp(12));
        card.Background = CardBackground();

        var words = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };

        var name = new TextView(this) { Text = part.Name };
        name.SetTextSize(ComplexUnitType.Sp, 16);
        name.SetTextColor(UiKit.PlainText(this));
        words.AddView(name);

        var about = new TextView(this) { Text = $"{Explain(part.Name)} · {DiagnosticsBundlePlan.Weigh(part.Bytes)}" };
        about.SetTextSize(ComplexUnitType.Sp, 13);
        about.Alpha = 0.7f;
        words.AddView(about, Below(this.Dp(4)));

        card.AddView(words, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var open = UiKit.CreateButton(this, AppStrings.DiagnosticsBundleOpen);
        open.SetTextSize(ComplexUnitType.Sp, 13);
        open.Click += (_, _) => Open(part);
        card.AddView(open);

        return card;
    }

    /// <summary>
    /// Сколько уйдёт. В обычном комплекте это вес частей: архив собирается только по «Отправить», и
    /// до нажатия его ещё нет. В полном режиме архив уже лежит, и показывается <b>его</b> размер —
    /// владелец просил число, которое реально уедет по мобильной связи, а не то, что весит на диске.
    /// </summary>
    private string Total()
    {
        if (!_fullLog)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.DiagnosticsBundleTotal,
                DiagnosticsBundlePlan.Weigh(DiagnosticsBundlePlan.TotalBytes(_parts)));
        }

        long packed = _archive.Length > 0 && new FileInfo(_archive) is { Exists: true } file ? file.Length : 0;

        return string.Format(
            CultureInfo.CurrentCulture, AppStrings.DiagnosticsFullTotal, DiagnosticsBundlePlan.Weigh(packed));
    }

    /// <summary>Что это за файл — словами, а не именем: имя говорит машине, строка под ним — человеку.</summary>
    private static string Explain(string name) => name switch
    {
        "diagnostics.log" => AppStrings.DiagnosticsBundleLog,
        "diagnostics.log.1" => AppStrings.DiagnosticsBundlePrevious,
        DiagnosticsBundle.FullLogFile => AppStrings.DiagnosticsFullPart,
        _ => AppStrings.DiagnosticsBundleRides,
    };

    /// <summary>
    /// Посмотреть файл — тем, что у человека есть для текста. Своего просмотрщика не заводим: он
    /// был бы третьим местом, где журнал показывается, и первым, где его показ отстанет.
    /// </summary>
    private void Open(DiagnosticsPart part)
    {
        // Дисковое имя не показываем: два прошедших вечера дадут два diagnostics.log в одной папке
        // «Загрузки», и получатель откроет первый попавшийся, а не только что присланный.
        string displayName = DiagnosticsBundle.DisplayName(part.Name);
        var uri = FileProvider.GetUriForFile(this, Authority, new Java.IO.File(part.Path), displayName);

        var view = new Intent(Intent.ActionView);
        view.SetDataAndType(uri, "text/plain");
        view.AddFlags(ActivityFlags.GrantReadUriPermission);

        StartActivity(Intent.CreateChooser(view, AppStrings.DiagnosticsBundleOpen));
    }

    private View Buttons()
    {
        var row = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };

        var cancel = UiKit.CreateButton(this, AppStrings.Cancel);
        cancel.Click += (_, _) => Finish();
        row.AddView(cancel, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var send = UiKit.CreateButton(this, AppStrings.DiagnosticsBundleSend);
        send.Enabled = _parts.Count > 0;
        send.Click += (_, _) => Share();
        row.AddView(send, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        {
            LeftMargin = this.Dp(10),
        });

        return row;
    }

    /// <summary>
    /// Упаковать и отдать системному диалогу. Получателя выбирает человек — своего сервера у нас
    /// нет и не будет: это и проще, и честнее.
    /// </summary>
    private void Share()
    {
        // В полном режиме архив собран при открытии — его вес человек уже прочёл на экране, и
        // пересобирать его значило бы отправить не то, что показали.
        string archive = _archive.Length > 0 ? _archive : DiagnosticsBundle.Pack(_parts);
        var uri = FileProvider.GetUriForFile(this, Authority, new Java.IO.File(archive));

        var send = new Intent(Intent.ActionSend);
        send.SetType("application/zip");
        send.PutExtra(Intent.ExtraStream, uri);
        send.AddFlags(ActivityFlags.GrantReadUriPermission);

        StartActivity(Intent.CreateChooser(send, AppStrings.SettingShareDiagnostics));
        Finish();
    }

    private LinearLayout.LayoutParams Below(int top) =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = top };

    private Android.Graphics.Drawables.Drawable CardBackground()
    {
        var drawable = new Android.Graphics.Drawables.GradientDrawable();
        drawable.SetShape(Android.Graphics.Drawables.ShapeType.Rectangle);
        drawable.SetCornerRadius(this.Dp(12));
        drawable.SetStroke(this.Dp(1), this.ShareBorder());
        return drawable;
    }
}
