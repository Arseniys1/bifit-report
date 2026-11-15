namespace BifitReport.Agent.Configuration;

public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    /// <summary>Включает фоновое копирование ротированных журналов кассы.</summary>
    public bool Enabled { get; set; }

    /// <summary>Корневой каталог архива. Для каждой кассы создаётся подкаталог с её именем.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Каталоги журналов: локальные или UNC-пути к папкам logs касс.</summary>
    public List<ArchiveSource> Sources { get; set; } = [];

    /// <summary>Маски файлов, которые нужно сохранять. Живой desktop.log намеренно не входит.</summary>
    public List<string> Patterns { get; set; } =
    [
        "desktop-*.log.gz",
        "chzn-*.log.gz",
        "launcher*.log"
    ];

    /// <summary>Не копировать файлы, изменённые только что: они ещё могут дописываться.</summary>
    public int MinFileAgeMinutes { get; set; } = 10;

    public int PollIntervalMinutes { get; set; } = 60;
}

public sealed class ArchiveSource
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}
