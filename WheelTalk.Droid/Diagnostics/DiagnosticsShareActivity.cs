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

    private IReadOnlyList<DiagnosticsPart> _parts = [];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Title = AppStrings.DiagnosticsBundleTitle;
        _parts = DiagnosticsBundle.Prepare();

        var root = BuildLayout();
        SetContentView(root);
        EdgeToEdge.Apply(this, root);
    }

    private View BuildLayout()
    {
        var scroll = new ScrollView(this);
        scroll.SetBackgroundColor(this.PageBackground());

        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        int pad = this.Dp(16);
        root.SetPadding(pad, this.Dp(20), pad, pad);

        var title = new TextView(this) { Text = AppStrings.DiagnosticsBundleTitle };
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

        var total = new TextView(this)
        {
            Text = string.Format(
                CultureInfo.CurrentCulture,
                AppStrings.DiagnosticsBundleTotal,
                DiagnosticsBundlePlan.Weigh(DiagnosticsBundlePlan.TotalBytes(_parts))),
        };
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

    /// <summary>Что это за файл — словами, а не именем: имя говорит машине, строка под ним — человеку.</summary>
    private static string Explain(string name) => name switch
    {
        "diagnostics.log" => AppStrings.DiagnosticsBundleLog,
        "diagnostics.log.1" => AppStrings.DiagnosticsBundlePrevious,
        _ => AppStrings.DiagnosticsBundleRides,
    };

    /// <summary>
    /// Посмотреть файл — тем, что у человека есть для текста. Своего просмотрщика не заводим: он
    /// был бы третьим местом, где журнал показывается, и первым, где его показ отстанет.
    /// </summary>
    private void Open(DiagnosticsPart part)
    {
        var uri = FileProvider.GetUriForFile(this, Authority, new Java.IO.File(part.Path));

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
        string archive = DiagnosticsBundle.Pack(_parts);
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
        drawable.SetStroke(this.Dp(1), Color.ParseColor("#40808080"));
        return drawable;
    }
}
