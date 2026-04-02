using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class ScenarioModel
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = "http";

    [YamlMember(Alias = "target")]
    public string Target { get; set; } = string.Empty;

    [YamlMember(Alias = "rps")]
    public int Rps { get; set; } = 10;

    [YamlMember(Alias = "duration")]
    public string Duration { get; set; } = "30s";

    [YamlMember(Alias = "startup_timeout")]
    public string StartupTimeout { get; set; } = "30s";

    [YamlMember(Alias = "timeline")]
    public List<TimelinePointModel>? Timeline { get; set; }
}
