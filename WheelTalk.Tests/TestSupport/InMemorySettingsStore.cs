using WheelTalk.Core.Settings;

namespace WheelTalk.Tests.TestSupport;

/// <summary>
/// The settings store without the database. What is under test in the core is which layer wins,
/// and SQLite has no opinion about that — it gets its own tests next to the schema.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _scopes = [];

    public IReadOnlyDictionary<string, string> Read(string scope) =>
        _scopes.TryGetValue(scope, out var values) ? values : new Dictionary<string, string>();

    public void Write(string scope, string key, string? value)
    {
        if (value is null)
        {
            if (_scopes.TryGetValue(scope, out var existing)) existing.Remove(key);
            return;
        }

        if (!_scopes.TryGetValue(scope, out var values))
        {
            _scopes[scope] = values = [];
        }

        values[key] = value;
    }
}
