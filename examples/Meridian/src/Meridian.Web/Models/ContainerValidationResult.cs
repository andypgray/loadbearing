namespace Meridian.Web.Models;

public sealed record ContainerValidationResult(
    string ContainerNumber,
    bool IsValid);