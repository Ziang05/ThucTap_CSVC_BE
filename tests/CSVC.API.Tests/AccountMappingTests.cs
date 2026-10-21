using System.Text.Json;
using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Accounts.Mapping;
using CSVC.UseCases.Accounts.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSVC.API.Tests;

public sealed class AccountMappingTests
{
    private readonly IMapper _mapper = new MapperConfiguration(configuration =>
        {
            configuration.AddProfile<AccountMappingProfile>();
            configuration.AddProfile<UseCases.Authentication.Mapping.AuthenticationMappingProfile>();
        }, NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public void Mapping_configuration_is_valid_and_profile_update_preserves_identity_and_department()
    {
        _mapper.ConfigurationProvider.AssertConfigurationIsValid();
        var profile = new UserProfile { FullName = "Old", DepartmentId = Guid.NewGuid() };
        var id = profile.Id;
        var department = profile.DepartmentId;
        _mapper.Map(new UpdateAccountProfileRequest
        {
            FullName = "  New   Name ", Phone = " 01234 ", DepartmentId = Guid.NewGuid()
        }, profile);
        Assert.Equal(id, profile.Id);
        Assert.Equal(department, profile.DepartmentId);
        Assert.Equal("New Name", profile.FullName);
        Assert.Equal("01234", profile.Phone);
    }

    [Fact]
    public void Public_responses_have_no_password_token_or_hash_properties()
    {
        foreach (var type in new[] { typeof(AccountListItemDto), typeof(AccountDetailDto), typeof(CreatedAccountDto), typeof(UseCases.Authentication.DTOs.AuthenticatedUserDto) })
        {
            Assert.DoesNotContain(type.GetProperties(), property =>
                property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
        }
        var json = JsonSerializer.Serialize(_mapper.Map<AccountDetailDto>(new AccountReadModel { Email = "test@example.com" }));
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("SessionTokenHash", json);
    }
}
