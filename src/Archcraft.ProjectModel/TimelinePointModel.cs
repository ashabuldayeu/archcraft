using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class TimelinePointModel
{
    [YamlMember(Alias = "at")]
    public string At { get; set; } = "0s";

    [YamlMember(Alias = "actions")]
    public List<TimelineActionModel> Actions { get; set; } = [];
}

public sealed class TimelineActionModel
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "duration")]
    public string? Duration { get; set; }

    // load
    [YamlMember(Alias = "target")]
    public object? Target { get; set; }

    [YamlMember(Alias = "endpoint")]
    public string? Endpoint { get; set; }

    [YamlMember(Alias = "rps")]
    public int Rps { get; set; }

    // inject_latency
    [YamlMember(Alias = "latency")]
    public string? Latency { get; set; }

    // inject_error
    [YamlMember(Alias = "error_rate")]
    public double ErrorRate { get; set; }
}

public sealed class TimelineTargetModel
{
    [YamlMember(Alias = "from")]
    public string From { get; set; } = string.Empty;

    [YamlMember(Alias = "to")]
    public string To { get; set; } = string.Empty;
}
