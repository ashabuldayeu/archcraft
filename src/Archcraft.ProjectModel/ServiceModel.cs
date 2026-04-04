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

    [YamlMember(Alias = "proxy")]
    public string? Proxy { get; set; }

    [YamlMember(Alias = "replicas")]
    public int Replicas { get; set; } = 1;

    [YamlMember(Alias = "synthetic")]
    public SyntheticServiceModel? Synthetic { get; set; }
}
