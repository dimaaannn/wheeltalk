namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// One tab in the screen-switch strip above <see cref="QuickSheet"/>'s command row
/// (android-plan-23-telemetry-screen-and-store.md §2.2: "корешки экранов, а не команда"). The strip
/// only shows which screen is picked and reports a tap — it never decides which screen is current;
/// that stays with whoever hands in the list, the same split <see cref="QuickSheetCommand"/> uses
/// for its own state.
/// </summary>
public sealed class QuickSheetScreen
{
    /// <summary>Big glyph drawn beside the label.</summary>
    public required string Icon { get; init; }

    /// <summary>Tab wording — the caller's, not the sheet's (see <see cref="QuickSheet.PinLabel"/> for why).</summary>
    public required string Label { get; init; }

    /// <summary>Read on every render: true highlights this tab as the current screen.</summary>
    public required Func<bool> IsSelected { get; init; }

    /// <summary>Reports the tap. Synchronous, unlike <see cref="QuickSheetCommand.Action"/> — switching
    /// screens has no delivery to confirm, so there is no fate for the sheet to hold on screen.</summary>
    public required Action Select { get; init; }
}
