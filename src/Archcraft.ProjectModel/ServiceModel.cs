using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class ServiceModel
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "image")]
    public string Image { get; set; } = string.Empty;

    [YamlMember(Alias = "port")]
    public int Port { get; set; }

    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }

    [YamlMember(Alias = "readiness")]
    public ReadinessModel? Readiness { get; set; }
}
