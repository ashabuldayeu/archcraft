namespace SynteticApi.Configuration;

public sealed class SynteticApiOptions
{
    public const string SectionName = "SynteticApi";

    public string ServiceName { get; set; } = "syntetic-api";
    public List<EndpointOptions> Endpoints { get; set; } = [];
}
