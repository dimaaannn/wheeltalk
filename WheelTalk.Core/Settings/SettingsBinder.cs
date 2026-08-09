namespace WheelTalk.Core.Settings;

/// <summary>
/// Keeps the live options objects agreeing with the layers. This is the piece the plan called the
/// heaviest: the options instances cannot be replaced when a wheel changes — decoders write their
/// reported values into the very same object, and the alert engine reads it — so they are
/// <em>updated in place</em>, every setting, every time the effective values move.
/// </summary>
public sealed class SettingsBinder
{
    private readonly LayeredSettings _settings;
    private readonly IReadOnlyList<SettingDescriptor> _descriptors;

    public SettingsBinder(LayeredSettings settings, IReadOnlyList<SettingDescriptor> descriptors)
    {
        _settings = settings;
        _descriptors = descriptors;
        _settings.Changed += Apply;
    }

    public IReadOnlyList<SettingDescriptor> Descriptors => _descriptors;

    /// <summary>
    /// Боевая область — колесо, по которому живут живые объекты. Отдаётся наружу только на чтение:
    /// правка её принадлежит выбору колеса, а не тому, кто её прочёл (план 29 §29.3). Нужна двоим —
    /// кнопке, которая правит соседнюю строку, и сводке настроек на корневом экране: обе говорят о
    /// том, чем приложение живёт, а не о том, что открыто на странице.
    /// </summary>
    public string LiveScope => _settings.Scope;

    /// <summary>
    /// The factory layer, read off the options objects while they still hold only what shipped in
    /// the package. Taken this way rather than by parsing the JSON a second time: the binding has
    /// already done that work, and two readers of one file eventually disagree about it.
    /// <para>
    /// What the wheel reports is left out. It has no layer at all — not even the bottom one — so
    /// that asking any layer about it comes back empty rather than with whatever the packaged file
    /// happened to say before a wheel had ever answered.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> FactoryDefaults(IEnumerable<SettingDescriptor> descriptors) =>
        descriptors
            .Where(d => !d.ReportedByWheel && !d.Transient && d.Kind != SettingKind.Action)
            .ToDictionary(d => d.Key, d => d.Current(), StringComparer.Ordinal);

    /// <summary>
    /// Pushes every resolved value into the live objects. Области у него нет и быть не может:
    /// живые объекты — это то, чем едут, и разрешаются они всегда по колесу, что бы ни было открыто
    /// на странице настроек (план 29 §29.3).
    /// </summary>
    public void Apply()
    {
        foreach (var descriptor in _descriptors)
        {
            // What the wheel reports is not ours to restore. Writing it back would fight the
            // decoder, and the decoder wins on the next frame anyway.
            if (descriptor.ReportedByWheel) continue;

            // Сеансовая настройка на то и сеансовая: восстановить её при старте значило бы не
            // сбросить — а именно сброса от неё и ждут.
            if (descriptor.Transient) continue;

            // У действия нет значения, которое можно восстановить, — а вызов Apply здесь означал
            // бы, что кнопка нажимается сама при каждом запуске.
            if (descriptor.Kind == SettingKind.Action) continue;

            if (_settings.Get(_settings.Scope, descriptor.Key, descriptor.Layer).Value is { } value)
            {
                descriptor.Apply(value);
            }
        }
    }

    /// <summary>
    /// An edit into <paramref name="scope"/>: into the layer it belongs to, and оттуда — в живой
    /// объект, но только если правленый слой и есть тот, по которому живут. Правка общего слоя при
    /// живом переопределении колеса меняет общий слой и на дороге не сказывается ничем — так и
    /// задумано: рамка «переопределено» на строке объясняет, почему число на экране не дрогнуло.
    /// <para>
    /// Область обязательна, значения по умолчанию у неё нет: забытый аргумент означал бы тихую
    /// запись не в тот слой, а это ровно та ошибка, ради которой затевался план 29 §29.3.
    /// </para>
    /// </summary>
    public void Set(SettingDescriptor descriptor, string value, string scope)
    {
        if (descriptor.ReportedByWheel) return;

        // Сеансовую применяем и забываем: слои её не видят, и следующий запуск начнётся с нуля.
        if (descriptor.Transient)
        {
            descriptor.Apply(value);
            descriptor.AfterEdit?.Invoke();
            return;
        }

        _settings.Set(scope, descriptor.Key, value, descriptor.Layer);

        // После записи, а не вместо неё: крючок вправе опереться на уже применённое значение
        // (_settings.Set поднимает Changed, а тот прогоняет Apply). И только здесь — это
        // единственное место, где точно известно, что значение изменил человек.
        descriptor.AfterEdit?.Invoke();
    }

    /// <summary>
    /// Правка соседней строки — по ключу: кнопка «рассчитать» подставляет число в настройку ряда
    /// (план 27 §27.4), а описания той строки у неё на руках нет. Через это же место, а не мимо:
    /// слой, крючок правки и признаки — всё то же самое, что у правки руками.
    /// <para>
    /// Пишет областью колеса, а не смотровой: ряд ячеек считается по кадру живого колеса, и число
    /// принадлежит ему — куда бы в этот момент ни смотрел переключатель страницы.
    /// </para>
    /// </summary>
    public void Set(string key, string value)
    {
        var descriptor = _descriptors.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Настройки с ключом «{key}» нет в каталоге.", nameof(key));

        Set(descriptor, value, _settings.Scope);
    }

    /// <summary>
    /// Обе команды строки — тоже отсюда, а не мимо. Пока страница ходила в слои напрямую, признаки
    /// описания охраняли ровно ничего: инвариант держался на том, что разметка не нарисует меню
    /// там, где не надо. Здесь же ключ и его признаки приходят вместе, одним объектом.
    /// </summary>
    public void ClearOverride(SettingDescriptor descriptor, string scope) =>
        _settings.ClearOverride(scope, descriptor.Key);

    /// <summary>Области нет и не бывает: общий слой один на всё приложение, снимать его больше неоткуда.</summary>
    public void ClearGlobal(SettingDescriptor descriptor) => _settings.ClearGlobal(descriptor.Key);

    public void PromoteToGlobal(SettingDescriptor descriptor, string scope) =>
        _settings.PromoteToGlobal(scope, descriptor.Key, descriptor.Layer);

    /// <summary>Что показать в строке: значение той области, которую смотрят, и слой, из которого оно пришло.</summary>
    public ResolvedSetting Read(SettingDescriptor descriptor, string scope) =>
        descriptor.ReportedByWheel
            // Whatever the wheel last said, not whatever a layer remembers.
            ? new ResolvedSetting(descriptor.Current(), SettingOrigin.Factory)
            : _settings.Get(scope, descriptor.Key, descriptor.Layer);

    /// <summary>
    /// The rows of one page, grouped, minus the ones their own condition hides. Conditions are
    /// evaluated here and not cached: a cascade has to close the moment its master switch does.
    /// <para>
    /// Обычные разделы идут первыми, разделы частных случаев — за ними, в объявленном порядке
    /// внутри каждой половины. Настройка для колёс без аппаратного ШИМ не должна стоять между
    /// двумя, которые крутят все.
    /// </para>
    /// <para>
    /// Строки, у которых общего значения не бывает (<see cref="SettingDescriptor.WheelOnly"/> — ряд
    /// ячеек и кнопка «рассчитать» к нему), в общей области не показываются: писать их там некуда,
    /// и строка, правка которой не делается, — обман. Раньше это решал делегат, спрашивавший о
    /// <see cref="LayeredSettings.Scope"/> — то есть страница спрашивала боевой рычаг; теперь она
    /// говорит, какую область смотрит, и ответ следует из самой области (план 29 §29.3).
    /// </para>
    /// </summary>
    public IEnumerable<IGrouping<string, SettingDescriptor>> Page(SettingsPage page, string scope) =>
        _descriptors
            .Where(d => d.Page == page && d.Visible && (scope.Length > 0 || !d.WheelOnly))
            .OrderBy(d => d.Advanced)
            .GroupBy(d => d.SectionKey);
}
