using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class ReadinessModel
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "/";

    [YamlMember(Alias = "timeout")]
    public string Timeout { get; set; } = "30s";
}
