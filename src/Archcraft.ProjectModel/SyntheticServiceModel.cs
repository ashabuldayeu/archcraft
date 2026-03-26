using YamlDotNet.Serialization;

namespace Archcraft.ProjectModel;

public sealed class SyntheticServiceModel
{
    [YamlMember(Alias = "adapters")]
    public List<string> Adapters { get; set; } = [];

    [YamlMember(Alias = "endpoints")]
    public List<SyntheticEndpointModel> Endpoints { get; set; } = [];
}

public sealed class SyntheticEndpointModel
{
    [YamlMember(Alias = "alias")]
    public string Alias { get; set; } = string.Empty;

    [YamlMember(Alias = "pipeline")]
    public List<SyntheticPipelineStepModel> Pipeline { get; set; } = [];
}

public sealed class SyntheticPipelineStepModel
{
    [YamlMember(Alias = "operation")]
    public string Operation { get; set; } = string.Empty;

    [YamlMember(Alias = "not-found-rate")]
    public double NotFoundRate { get; set; }

    [YamlMember(Alias = "fallback")]
    public List<SyntheticPipelineStepModel> Fallback { get; set; } = [];

    [YamlMember(Alias = "children")]
    public List<SyntheticPipelineStepModel> Children { get; set; } = [];
}
