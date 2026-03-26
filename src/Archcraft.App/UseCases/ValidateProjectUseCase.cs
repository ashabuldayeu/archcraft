using Archcraft.Contracts;
using Archcraft.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Archcraft.App.UseCases;

public sealed class ValidateProjectUseCase
{
    private readonly IProjectLoader _loader;
    private readonly ITopologyValidator _validator;
    private readonly ILogger<ValidateProjectUseCase> _logger;

    public ValidateProjectUseCase(
        IProjectLoader loader,
        ITopologyValidator validator,
        ILogger<ValidateProjectUseCase> logger)
    {
        _loader = loader;
        _validator = validator;
        _logger = logger;
    }

    public async Task<ValidationResult> ExecuteAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating project file: {Path}", projectFilePath);

        ProjectDefinition project;
        try
        {
            project = await _loader.LoadAsync(projectFilePath, cancellationToken);
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure($"Failed to parse project file: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(project.Name))
            return ValidationResult.Failure("Project 'name' is required.");

        if (project.Services.Count == 0)
            return ValidationResult.Failure("Project must define at least one service.");

        ValidationResult topologyResult = _validator.Validate(project.Topology, project.Services);
        if (!topologyResult.IsValid)
            return topologyResult;

        _logger.LogInformation("Project '{Name}' is valid.", project.Name);
        return ValidationResult.Success();
    }
}
