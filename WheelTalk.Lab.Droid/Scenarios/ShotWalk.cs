using WheelTalk.Lab.Droid;

namespace WheelTalk.Lab.Droid.Scenarios;

/// <summary>
/// Обход: все варианты во всех характерных точках сценария, по одному шагу на касание экрана.
/// Смысл в том, что кадр один и тот же — сравнивать варианты, снятые в разные моменты поездки,
/// нельзя, разница окажется в данных, а не в панели.
/// <para>
/// Снимает при этом не приложение, а <c>adb</c> со стороны хоста (<c>tools/lab-shots.ps1</c>):
/// снимок средствами самого приложения на эмуляторе отставал от экрана на несколько шагов.
/// Порядок шагов приложение выкладывает в <c>order.txt</c>, чтобы хост знал, как называть файлы,
/// и не зависел от координат кнопок.
/// </para>
/// <para>Извлечено из <c>LabActivity</c> (план 14, А3) — тело обхода перенесено как есть.</para>
/// </summary>
public sealed class ShotWalk
{
    private IReadOnlyList<(DashboardCatalog.Variant Variant, TimelineMark Mark)> _steps = [];
    private int _step = -1;

    public int Count => _steps.Count;

    public bool IsWalking => _step >= 0;

    /// <summary>Начинает обход заново: строит список шагов и пишет порядок в order.txt (для хоста).</summary>
    public void Start(string scenarioId, Timeline timeline)
    {
        var marks = timeline.Marks.Count > 0
            ? timeline.Marks
            : [new TimelineMark("начало", TimeSpan.Zero)];

        _steps = DashboardCatalog.All
            .SelectMany(variant => marks.Select(mark => (Variant: variant, Mark: mark)))
            .ToList();

        string folder = LabFiles.ShotsFolder(scenarioId);
        File.WriteAllLines(
            System.IO.Path.Combine(folder, "order.txt"),
            _steps.Select(step => $"{step.Variant.Id}-{step.Mark.Name.Replace(' ', '-')}"));

        _step = -1;
    }

    /// <summary>Следующий шаг обхода, либо null, когда обход пройден (и обход сброшен).</summary>
    public (DashboardCatalog.Variant Variant, TimelineMark Mark)? Advance()
    {
        _step++;
        if (_step >= _steps.Count)
        {
            _step = -1;
            return null;
        }

        return _steps[_step];
    }
}
