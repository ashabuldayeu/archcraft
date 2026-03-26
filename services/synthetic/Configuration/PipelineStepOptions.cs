namespace SynteticApi.Configuration;

public sealed class PipelineStepOptions
{
    public required string Operation { get; set; }
    public string? Target { get; set; }
    public double NotFoundRate { get; set; } = 0.0;
    public List<PipelineStepOptions> Fallback { get; set; } = [];
    public List<PipelineStepOptions> Children { get; set; } = [];
}
