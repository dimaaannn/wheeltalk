using Android.Views;

namespace WheelTalk.Dashboard.Droid.Screen;

/// <summary>
/// Одиночный тап с координатами — подтверждённый, чтобы не спорить с двойным. Жест ловит хозяин
/// экрана (у него <c>DispatchTouchEvent</c>), а во что касание попало, решает сам экран
/// (<see cref="IMainScreen.Tap"/>): метки нарисованы на его канве, и координаты хозяину знать нечего.
/// <para>
/// Живёт в библиотеке рядом с <see cref="SwipeUpFromEdgeListener"/> по той же причине: и приложение,
/// и стенд ловят один и тот же жест, а две копии одного слушателя разошлись бы порогом или тем, что
/// одна вернула бы <c>true</c> и съела касание.
/// </para>
/// </summary>
public sealed class SingleTapListener(Action<float, float> onTap) : GestureDetector.SimpleOnGestureListener
{
    public override bool OnSingleTapConfirmed(MotionEvent? e)
    {
        if (e is null) return false;

        onTap(e.GetX(), e.GetY());
        // false: касание идёт дальше своим чередом — экран лишь узнал о нём, а не перехватил.
        return false;
    }
}
