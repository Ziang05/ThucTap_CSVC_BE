using System.ComponentModel.DataAnnotations;
using CSVC.Core.Exceptions;
using CSVC.UseCases.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Tests;

[ApiController]
[Route("test-probe")]
public sealed class ProbeController : ControllerBase
{
    [HttpGet("unexpected")]
    public IActionResult Unexpected() => throw new InvalidOperationException("sensitive-database-password");

    [HttpGet("domain")]
    public IActionResult Domain() => throw new DomainException("RULE_VIOLATION", "A domain rule rejected the operation.");

    [HttpGet("failure/{kind}")]
    public IActionResult Failure(FailureKind kind) => throw new UseCaseException(kind, "EXPECTED_FAILURE", "Expected failure.");

    [HttpPost("validation")]
    public IActionResult Validate(ProbeRequest request) => Ok(request);
}

public sealed class ProbeRequest
{
    [Required]
    public string? Name { get; init; }
}
