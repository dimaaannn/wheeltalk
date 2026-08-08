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

    /// <summary>Pushes every resolved value into the live objects.</summary>
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

            if (_settings.Get(descriptor.Key, descriptor.Layer).Value is { } value) descriptor.Apply(value);
        }
    }

    /// <summary>An edit: into the layer it belongs to, then straight into the live object.</summary>
    public void Set(SettingDescriptor descriptor, string value)
    {
        if (descriptor.ReportedByWheel) return;

        // Сеансовую применяем и забываем: слои её не видят, и следующий запуск начнётся с нуля.
        if (descriptor.Transient)
        {
            descriptor.Apply(value);
            descriptor.AfterEdit?.Invoke();
            return;
        }

        _settings.Set(descriptor.Key, value, descriptor.Layer);

        // После записи, а не вместо неё: крючок вправе опереться на уже применённое значение
        // (_settings.Set поднимает Changed, а тот прогоняет Apply). И только здесь — это
        // единственное место, где точно известно, что значение изменил человек.
        descriptor.AfterEdit?.Invoke();
    }

    /// <summary>
    /// Правка соседней строки — по ключу: кнопка «рассчитать» подставляет число в настройку ряда
    /// (план 27 §27.4), а описания той строки у неё на руках нет. Через это же место, а не мимо:
    /// слой, крючок правки и признаки — всё то же самое, что у правки руками.
    /// </summary>
    public void Set(string key, string value)
    {
        var descriptor = _descriptors.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Настройки с ключом «{key}» нет в каталоге.", nameof(key));

        Set(descriptor, value);
    }

    /// <summary>
    /// Обе команды строки — тоже отсюда, а не мимо. Пока страница ходила в слои напрямую, признаки
    /// описания охраняли ровно ничего: инвариант держался на том, что разметка не нарисует меню
    /// там, где не надо. Здесь же ключ и его признаки приходят вместе, одним объектом.
    /// </summary>
    public void ClearOverride(SettingDescriptor descriptor) => _settings.ClearOverride(descriptor.Key);

    public void ClearGlobal(SettingDescriptor descriptor) => _settings.ClearGlobal(descriptor.Key);

    public void PromoteToGlobal(SettingDescriptor descriptor) =>
        _settings.PromoteToGlobal(descriptor.Key, descriptor.Layer);

    public ResolvedSetting Read(SettingDescriptor descriptor) =>
        descriptor.ReportedByWheel
            // Whatever the wheel last said, not whatever a layer remembers.
            ? new ResolvedSetting(descriptor.Current(), SettingOrigin.Factory)
            : _settings.Get(descriptor.Key, descriptor.Layer);

    /// <summary>
    /// The rows of one page, grouped, minus the ones their own condition hides. Conditions are
    /// evaluated here and not cached: a cascade has to close the moment its master switch does.
    /// <para>
    /// Обычные разделы идут первыми, разделы частных случаев — за ними, в объявленном порядке
    /// внутри каждой половины. Настройка для колёс без аппаратного ШИМ не должна стоять между
    /// двумя, которые крутят все.
    /// </para>
    /// </summary>
    public IEnumerable<IGrouping<string, SettingDescriptor>> Page(SettingsPage page) =>
        _descriptors
            .Where(d => d.Page == page && d.Visible)
            .OrderBy(d => d.Advanced)
            .GroupBy(d => d.SectionKey);
}
