using AutoMapper;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Authentication.DTOs;

namespace CSVC.UseCases.Authentication.Mapping;

public sealed class AuthenticationMappingProfile : Profile
{
    public AuthenticationMappingProfile() => CreateMap<AccountDetailDto, AuthenticatedUserDto>();
}
