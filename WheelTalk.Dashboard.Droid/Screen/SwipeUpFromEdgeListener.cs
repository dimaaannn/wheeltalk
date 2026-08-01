using Android.Views;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Свайп вверх от нижней кромки открывает шторку (quick-commands-design.md §2) — «только от
/// кромки», проверено по точке начала жеста в экранных координатах, чтобы не спорить с другими
/// жестами в середине экрана. В библиотеке, потому что зона вызова — часть механики шторки:
/// стенд обязан открывать её тем же жестом, что и приложение, а не своей копией порогов.
/// </summary>
public sealed class SwipeUpFromEdgeListener(Action onSwipeUp, int screenHeightPx, int edgeZonePx)
    : GestureDetector.SimpleOnGestureListener
{
    private const int MinDistanceDp = 40;
    private const int MinVelocity = 200;

    public override bool OnFling(MotionEvent? e1, MotionEvent? e2, float velocityX, float velocityY)
    {
        if (e1 is null || e2 is null) return false;
        if (e1.RawY < screenHeightPx - edgeZonePx) return false;

        float dx = e2.RawX - e1.RawX;
        float dy = e2.RawY - e1.RawY;
        if (dy < -MinDistanceDp && Math.Abs(dy) > Math.Abs(dx) && Math.Abs(velocityY) > MinVelocity)
        {
            onSwipeUp();
            return true;
        }

        return false;
    }
}
