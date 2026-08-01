namespace WheelTalk.Core.Settings;

/// <summary>
/// Where user settings are kept, as flat text under a scope. The core decides what a value means
/// and which layer wins; the implementation only stores and returns strings.
/// <para>
/// Text rather than typed values on purpose: the store has to hold settings of four different
/// kinds without knowing about any of them, and a database column that is sometimes a number and
/// sometimes a flag is a column nobody can query. Parsing belongs where the meaning is.
/// </para>
/// </summary>
public interface ISettingsStore
{
    /// <summary>Everything stored under one scope. Reading a whole scope at once is what makes layering cheap.</summary>
    IReadOnlyDictionary<string, string> Read(string scope);

    /// <summary>Writes one value, or removes it when <paramref name="value"/> is null.</summary>
    void Write(string scope, string key, string? value);
}
