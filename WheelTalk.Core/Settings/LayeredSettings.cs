namespace WheelTalk.Core.Settings;

/// <summary>Which layer the value on screen actually came from.</summary>
public enum SettingOrigin
{
    /// <summary>Shipped with the app and never written to. The last answer, and the way back when everything is confused.</summary>
    Factory,

    /// <summary>The user's own default, in force for every wheel.</summary>
    Global,

    /// <summary>Set for this wheel specifically, on top of the global one.</summary>
    Wheel,
}

/// <summary>
/// В каких слоях настройке позволено жить. Два края здесь не симметричные оговорки, а два разных
/// вида вреда: общее значение там, где различие физически невозможно (звук в кармане у одного
/// райдера), — и общее значение там, где различие обязательно (ряд ячеек: 20S у одного колеса,
/// 16S у другого).
/// </summary>
public enum SettingLayer
{
    /// <summary>Обычная: общее значение, поверх него — своё у колеса.</summary>
    Any,

    /// <summary>Только общее. Своё у колеса не заводится и снимается, если было.</summary>
    GlobalOnly,

    /// <summary>Только своё у колеса. Общий слой не пишется и <b>не читается</b> — даже если в нём что-то лежит.</summary>
    WheelOnly,
}

/// <param name="Value">Null only when no layer has the key at all — an unknown setting, not an empty one.</param>
public readonly record struct ResolvedSetting(string? Value, SettingOrigin Origin)
{
    public bool IsOverridden => Origin == SettingOrigin.Wheel;
}

/// <summary>
/// The three layers of a setting and the rules for moving a value between them. This is the only
/// non-trivial logic in the settings work, which is why it lives in the core behind a store it
/// does not implement: everything written in the Android project is testable by nothing at all.
/// <para>
/// Factory defaults sit at the bottom and are never written; the user's own default is next; a
/// wheel's own value is on top and exists only where someone explicitly set one. A row on screen
/// shows the effective value and which of the three it came from — without that, layering turns
/// into "why is this number different here" with no answer available.
/// </para>
/// </summary>
public sealed class LayeredSettings
{
    /// <summary>The scope a global value is stored under. Empty rather than a sentinel word: no MAC can collide with it.</summary>
    public const string GlobalScope = "";

    private readonly ISettingsStore _store;
    private readonly IReadOnlyDictionary<string, string> _factory;

    private IReadOnlyDictionary<string, string>? _global;
    private IReadOnlyDictionary<string, string>? _wheel;
    private string _scope = GlobalScope;

    public LayeredSettings(ISettingsStore store, IReadOnlyDictionary<string, string> factoryDefaults)
    {
        _store = store;
        _factory = factoryDefaults;
    }

    /// <summary>
    /// The wheel whose values are on top, by MAC. Empty means there is no wheel yet, and then
    /// editing writes the global value — there is nothing else it could sensibly mean.
    /// </summary>
    public string Scope
    {
        get => _scope;
        set
        {
            if (_scope == value) return;

            _scope = value;
            _wheel = null;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Raised whenever the effective values may have moved — a write, or a different wheel. The
    /// live options objects are rebuilt from this: they are what the decoders and the alert engine
    /// read, and they have to be updated in place rather than replaced.
    /// </summary>
    public event Action? Changed;

    public ResolvedSetting Get(string key, SettingLayer layer = SettingLayer.Any)
    {
        if (_scope.Length > 0 && Wheel().TryGetValue(key, out string? own))
        {
            return new ResolvedSetting(own, SettingOrigin.Wheel);
        }

        // Настройка колеса общий слой не читает вовсе: колесо, которому ряд не задавали, должно
        // получить заводское «не задано», а не число соседнего колеса. Это не перестраховка от
        // собственной записи — в общем слое значение могло оказаться и другим путём.
        if (layer != SettingLayer.WheelOnly && Global().TryGetValue(key, out string? shared))
        {
            return new ResolvedSetting(shared, SettingOrigin.Global);
        }

        return new ResolvedSetting(_factory.GetValueOrDefault(key), SettingOrigin.Factory);
    }

    /// <summary>
    /// An edit. It lands on the wheel when there is one — that is what "this wheel's setting" means
    /// and how an override comes into being — and on the global value when there is not.
    /// <para>
    /// <see cref="SettingLayer.GlobalOnly"/> is for the settings that cannot differ between two
    /// wheels at all: the alert channels belong to the phone and its rider, the retry delays to the
    /// app, the border width to the screen. Storing one of those per wheel would put a frame and a
    /// menu on a difference that physically cannot exist.
    /// </para>
    /// <para>
    /// <see cref="SettingLayer.WheelOnly"/> — обратный случай: колеса нет, значит писать некуда, и
    /// правка не делается вовсе. Молча: единственный путь сюда — правка в общей области, а строку
    /// такой настройки там не показывают.
    /// </para>
    /// </summary>
    public void Set(string key, string value, SettingLayer layer = SettingLayer.Any)
    {
        if (layer == SettingLayer.GlobalOnly)
        {
            Store(GlobalScope, key, value, layer);
            // Переопределение у такого ключа всё же может лежать в базе — заведённое до того, как
            // признак появился. Не снять его значит писать в слой, который всё равно перекрыт:
            // правка выглядела бы не сработавшей.
            if (_scope.Length > 0) _store.Write(_scope, key, null);
        }
        else
        {
            if (layer == SettingLayer.WheelOnly && _scope.Length == 0) return;

            Store(CurrentScope(), key, value, layer);
        }

        Invalidate();
    }

    /// <summary>
    /// "Back to the global value": drops this wheel's own value so the global one shows through
    /// again. Does nothing when there was no override, which is also when the command is not
    /// offered.
    /// </summary>
    public void ClearOverride(string key)
    {
        if (_scope.Length == 0) return;

        _store.Write(_scope, key, null);
        Invalidate();
    }

    /// <summary>
    /// "Back to the factory value": drops the user's own default, leaving what shipped in the
    /// package. Without it the bottom layer is only a layer on paper — one edit with no wheel
    /// selected would put a value in the global layer for good, and §2.1 calls the factory layer
    /// the way back when everything is confused.
    /// </summary>
    public void ClearGlobal(string key)
    {
        _store.Write(GlobalScope, key, null);
        Invalidate();
    }

    /// <summary>
    /// "Overwrite the default": makes this wheel's value the global one — and drops the override
    /// while doing it. Keeping both would leave a second copy of a value that is now shared, which
    /// is a disagreement waiting to happen rather than a setting.
    /// </summary>
    public void PromoteToGlobal(string key, SettingLayer layer = SettingLayer.Any)
    {
        // «Сделать значением по умолчанию» для настройки колеса — это и есть та самая коллизия,
        // от которой её оберегают: 20S уехали бы на все колёса разом.
        if (layer == SettingLayer.WheelOnly) return;

        string? value = Get(key, layer).Value;
        if (value is null) return;

        Store(GlobalScope, key, value, layer);
        if (_scope.Length > 0) _store.Write(_scope, key, null);
        Invalidate();
    }

    /// <summary>Every key any layer knows about — what a page needs to draw itself completely.</summary>
    public IReadOnlyCollection<string> Keys()
    {
        var keys = new HashSet<string>(_factory.Keys, StringComparer.Ordinal);
        keys.UnionWith(Global().Keys);
        if (_scope.Length > 0) keys.UnionWith(Wheel().Keys);
        return keys;
    }

    /// <summary>Forces the next read to go to the store. For when someone else wrote to it.</summary>
    public void Reload()
    {
        _global = null;
        _wheel = null;
        Changed?.Invoke();
    }

    private string CurrentScope() => _scope.Length > 0 ? _scope : GlobalScope;

    /// <summary>
    /// Writes a value into one layer, or removes it when the layer below already says the same
    /// thing. A copy of the value underneath is not an override, it is a second instance of it:
    /// the row would stay framed for good and the command to drop it would visibly change nothing.
    /// The same reasoning <see cref="PromoteToGlobal"/> follows when it clears the override it just
    /// copied upwards. Comparing text is safe — a descriptor renders each value one way only.
    /// </summary>
    private void Store(string scope, string key, string value, SettingLayer layer) =>
        _store.Write(scope, key, value == Underlying(scope, key, layer) ? null : value);

    /// <summary>
    /// What <paramref name="scope"/> would show if it held nothing for this key. У настройки колеса
    /// под ней сразу заводское: общий слой она не читает, и сверяться с ним значило бы стереть
    /// правку, совпавшую с чужим числом.
    /// </summary>
    private string? Underlying(string scope, string key, SettingLayer layer) =>
        scope.Length > 0 && layer != SettingLayer.WheelOnly && Global().TryGetValue(key, out string? shared)
            ? shared
            : _factory.GetValueOrDefault(key);

    private IReadOnlyDictionary<string, string> Global() => _global ??= _store.Read(GlobalScope);

    private IReadOnlyDictionary<string, string> Wheel() => _wheel ??= _store.Read(_scope);

    private void Invalidate()
    {
        _global = null;
        _wheel = null;
        Changed?.Invoke();
    }
}
