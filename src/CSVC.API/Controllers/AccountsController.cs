using CSVC.UseCases.Accounts;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CSVC.API.Controllers;

[Route("api/v1/accounts")]
public sealed class AccountsController(IAccountService accounts) : AuthenticatedApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AccountListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AccountListItemDto>>> List([FromQuery] AccountListQuery query, CancellationToken cancellationToken)
        => Ok(await accounts.ListAsync(query, cancellationToken));

    [HttpGet("assignees")]
    [ProducesResponseType<IReadOnlyList<AssignableAccountDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AssignableAccountDto>>> ListAssignable([FromQuery] Guid departmentId, CancellationToken cancellationToken)
        => Ok(await accounts.ListAssignableAsync(departmentId, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AccountDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await accounts.GetAsync(id, cancellationToken));

    [HttpGet("me")]
    [ProducesResponseType<AccountDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDetailDto>> GetCurrent(CancellationToken cancellationToken)
        => Ok(await accounts.GetCurrentAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreatedAccountDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedAccountDto>> Create(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = account.Id }, account);
    }

    [HttpPut("{id:guid}/profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateProfile(Guid id, UpdateAccountProfileRequest request, CancellationToken cancellationToken)
    {
        await accounts.UpdateProfileAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpPut("me/profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateCurrentProfile(UpdateAccountProfileRequest request, CancellationToken cancellationToken)
    {
        await accounts.UpdateCurrentProfileAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPut("me/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await accounts.ChangePasswordAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPassword(Guid id, SetAccountPasswordRequest request, CancellationToken cancellationToken)
    {
        await accounts.SetPasswordAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken cancellationToken)
    {
        await accounts.SetLockedAsync(id, true, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken cancellationToken)
    {
        await accounts.SetLockedAsync(id, false, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("password-reset-requests")]
    [ProducesResponseType<PasswordResetAcceptedDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PasswordResetAcceptedDto>> RequestPasswordReset(RequestPasswordResetRequest request, CancellationToken cancellationToken)
    {
        await accounts.RequestPasswordResetAsync(request, cancellationToken);
        return Accepted(new PasswordResetAcceptedDto());
    }

    [AllowAnonymous]
    [HttpPost("password-resets")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CompletePasswordReset(CompletePasswordResetRequest request, CancellationToken cancellationToken)
    {
        await accounts.CompletePasswordResetAsync(request, cancellationToken);
        return NoContent();
    }
}
