using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class ConnectionModel
{
    [YamlMember(Alias = "from")]
    public string From { get; set; } = string.Empty;

    [YamlMember(Alias = "to")]
    public string To { get; set; } = string.Empty;

    [YamlMember(Alias = "protocol")]
    public string Protocol { get; set; } = "http";

    [YamlMember(Alias = "port")]
    public int Port { get; set; } = 0;

    [YamlMember(Alias = "alias")]
    public string? Alias { get; set; }

    [YamlMember(Alias = "via")]
    public string? Via { get; set; }
}
