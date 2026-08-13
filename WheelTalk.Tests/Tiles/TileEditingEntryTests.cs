using WheelTalk.Tests.TestSupport;

namespace WheelTalk.Tests.Tiles;

/// <summary>
/// Как входят в правку экрана «Цифры» — <b>долгим тапом по чему угодно на сетке</b>, включая голый
/// фон (решение владельца 12.08.2026: «долгий тап должен вызывать редактор и при клике на фон»).
/// <para>
/// <b>Случившаяся поломка.</b> Вход спрашивал плитку под пальцем (<c>FindChildViewUnder</c>), а
/// пустая раскладка законна и переживает перезапуск — человек, убравший все плитки, оставался с
/// полем, за которое не ухватиться: «если удалить все таблички — в редактор не зайти». Выход из
/// пустоты обязан быть, иначе экран теряется целиком.
/// </para>
/// <para>
/// <b>Почему замок по исходнику.</b> Жест живёт в android-библиотеке, поднять её отсюда нельзя, а
/// стеречь надо не арифметику, а решение: чем ловится долгий тап и чем он разведён с перетаскиванием
/// (тот же приём, что у <see cref="TileTapTests"/>).
/// </para>
/// </summary>
public class TileEditingEntryTests
{
    private static string Screen() =>
        RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TilesScreen.cs");

    /// <summary>
    /// Вход не спрашивает, что под пальцем: ни координат, ни плитки. Единственный его отказ — «уже
    /// правим», и это же разводит его с перетаскиванием.
    /// </summary>
    [Fact]
    public void A_long_press_anywhere_on_the_grid_opens_editing()
    {
        string screen = Screen();

        Assert.Contains("private void LongPress()", screen);

        string press = RepoFiles.MethodBody(screen, "private void LongPress()");

        Assert.DoesNotContain("FindChildViewUnder", press);
        Assert.Contains("if (_editing) return;", press);
        Assert.Contains("SetEditing(true)", press);

        // Правку начинают со снимка: «отменить» возвращает раскладку, какой она была на входе.
        Assert.Contains("_beforeEditing = _adapter.Snapshot();", press);
    }

    /// <summary>
    /// Слушают <b>сам список</b>, а не плитки: у пустой раскладки плиток нет вовсе, и слушатель,
    /// висящий на них, молчал бы ровно тогда, когда он нужнее всего.
    /// </summary>
    [Fact]
    public void The_gesture_is_heard_by_the_list_itself_and_not_by_its_tiles()
    {
        string screen = Screen();

        Assert.Contains("_list.AddOnItemTouchListener(new TileTouch(context, this));", screen);
        Assert.Contains("public override void OnLongPress(MotionEvent e) => _screen.LongPress();", screen);
    }

    /// <summary>
    /// Перетаскивание не спорит со входом: оно живёт только <b>в</b> правке, вход — только вне её.
    /// Разведены режимом, а не координатой, — значит ни в одной точке экрана одно не случится
    /// вместо другого.
    /// </summary>
    [Fact]
    public void Dragging_and_the_entry_never_answer_the_same_press()
    {
        string screen = Screen();

        Assert.Contains("public override bool IsLongPressDragEnabled => screen._editing;", screen);
        Assert.Contains("if (_editing) return;", RepoFiles.MethodBody(screen, "private void LongPress()"));
    }

    /// <summary>
    /// Из пустоты не только входят, но и собирают заново: «+» полосы правки заводит плитку, не
    /// оглядываясь на то, есть ли уже хоть одна.
    /// </summary>
    [Fact]
    public void An_empty_layout_can_be_built_up_again()
    {
        string screen = Screen();

        Assert.Contains("Button(context, palette, \"+\", () => ShowEditor(null))", screen);

        // Новая плитка — та же дорога, что и правка существующей, только без номера.
        string editor = RepoFiles.MethodBody(screen, "private void ShowEditor(int? position)");

        Assert.Contains("_adapter.Add(saved);", editor);
        Assert.Contains("position is { } removed ?", editor);

        // Заведение плитки ничего не знает о числе прежних: список пуст — встанет первой.
        string add = RepoFiles.MethodBody(screen, "public void Add(MetricTile tile)");

        Assert.Contains("_tiles.Add(entry);", add);
        Assert.DoesNotContain("_tiles.Count == 0", add);
    }

    /// <summary>
    /// Пустая раскладка — законное состояние, а не сбой: она переживает перезапуск, и зашитая на её
    /// место не воскресает. Ради этого вход и чинили — иначе достаточно было бы возвращать плитки.
    /// </summary>
    [Fact]
    public void An_empty_layout_is_a_lawful_state_that_survives_a_restart()
    {
        string json = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TileLayoutJson.cs");
        string read = RepoFiles.MethodBody(json, "public static IReadOnlyList<MetricTile>? Read(string? json)");

        // Умолчание зовут только тогда, когда сохранённого нет или оно не разобралось, — но не
        // тогда, когда человек сохранил пустоту.
        Assert.Contains("if (read is null) return null;", read);
        Assert.DoesNotContain("tiles.Count == 0", read);

        // А зашитая раскладка встаёт лишь там, где хранилище смолчало.
        Assert.Contains("layout?.Load() ?? TilesLayout.Fixed", Screen());
    }

    /// <summary>
    /// Новая плитка предлагается <b>«Числом» со скоростью</b>, а не «Пустом» (решение владельца
    /// 13.08.2026): «+» → «Сохранить» кладёт живую плитку. Дырку ставят решением, а не тем, что
    /// забыли сменить вид, — и из пустого экрана, где «+» жмут первым делом, это заметнее всего.
    /// </summary>
    [Fact]
    public void A_new_tile_is_offered_as_a_number_with_speed_and_not_as_a_void()
    {
        string editor = RepoFiles.Read("WheelTalk.Dashboard.Droid/Screen/Tiles/TileEditor.cs");

        // Вид по умолчанию — «Число» (позиция 0), и новая плитка пустой не считается: иначе меню
        // открылось бы с погашенной строкой величины.
        Assert.Contains("null => 0,", editor);
        Assert.DoesNotContain("null => 4", editor);
        Assert.Contains("bool empty = tile?.Kind == TileKind.Empty;", editor);

        // Величина по умолчанию — первая в каталоге, и это скорость. Настоящая проверка, не чтение
        // исходника: порядок каталога — тоже решение (план 23 §3.3), и съехать он может молча.
        Assert.Equal("speed", WheelTalk.Core.Metrics.MetricCatalogue.All[0].Id);
    }
}
