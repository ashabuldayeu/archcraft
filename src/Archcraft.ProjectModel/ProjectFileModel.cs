using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class ProjectFileModel
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "services")]
    public List<ServiceModel> Services { get; set; } = [];

    [YamlMember(Alias = "connections")]
    public List<ConnectionModel> Connections { get; set; } = [];

    [YamlMember(Alias = "adapters")]
    public List<AdapterModel> Adapters { get; set; } = [];

    [YamlMember(Alias = "scenarios")]
    public List<ScenarioModel> Scenarios { get; set; } = [];

    [YamlMember(Alias = "observability")]
    public ObservabilityModel? Observability { get; set; }
}
