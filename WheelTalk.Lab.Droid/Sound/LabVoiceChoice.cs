namespace WheelTalk.Lab.Droid.Sound;

/// <summary>
/// Что выбрано на выезде. Файлом, а не в памяти стенда: выбор делается на улице, а стенд к тому
/// времени успевает и уйти в фон, и быть убитым — на EMUI это происходит само. Выбор, переживший
/// только сеанс, к моменту разбора дома уже не существует.
/// </summary>
public static class LabVoiceChoice
{
    private static string Path => System.IO.Path.Combine(LabFiles.Root, "alarm-voice.txt");

    /// <summary>Запомненный вариант, либо <c>null</c>, если ещё не выбирали.</summary>
    public static AlarmVoice? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            string id = File.ReadAllText(Path).Trim();
            return AlarmVoices.All.FirstOrDefault(v => v.Id == id);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void Save(AlarmVoice voice) => File.WriteAllText(Path, voice.Id);
}
