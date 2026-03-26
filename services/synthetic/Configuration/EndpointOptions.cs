namespace SynteticApi.Configuration;

public sealed class EndpointOptions
{
    public required string Alias { get; set; }
    public List<PipelineStepOptions> Pipeline { get; set; } = [];
}
