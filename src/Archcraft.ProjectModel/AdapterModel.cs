using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class AdapterModel
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "image")]
    public string Image { get; set; } = string.Empty;

    [YamlMember(Alias = "port")]
    public int Port { get; set; }

    [YamlMember(Alias = "technology")]
    public string Technology { get; set; } = string.Empty;

    [YamlMember(Alias = "connects_to")]
    public string? ConnectsTo { get; set; }

    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }
}
