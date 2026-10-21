using AutoMapper;
using CSVC.Core.Accounts;
using CSVC.Core.Common;
using CSVC.UseCases.Accounts.DTOs;
using CSVC.UseCases.Accounts.Models;

namespace CSVC.UseCases.Accounts.Mapping;

public sealed class AccountMappingProfile : Profile
{
    public AccountMappingProfile()
    {
        CreateMap<CreateAccountRequest, Account>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.AccountCode, o => o.Ignore())
            .ForMember(d => d.UserProfileId, o => o.Ignore())
            .ForMember(d => d.Email, o => o.MapFrom(s => TextNormalization.Email(s.Email)))
            .ForMember(d => d.PasswordHash, o => o.Ignore())
            .ForMember(d => d.IsLock, o => o.Ignore());
        CreateMap<CreateAccountRequest, UserProfile>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.BirthDate, o => o.Ignore())
            .ForMember(d => d.FullName, o => o.MapFrom(s => TextNormalization.Name(s.FullName)))
            .ForMember(d => d.Phone, o => o.MapFrom(s => s.Phone == null ? null : s.Phone.Trim()));
        CreateMap<UpdateAccountProfileRequest, UserProfile>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.DepartmentId, o => o.Ignore())
            .ForMember(d => d.FullName, o => o.MapFrom(s => TextNormalization.Name(s.FullName)))
            .ForMember(d => d.Phone, o => o.MapFrom(s => s.Phone == null ? null : s.Phone.Trim()));
        CreateMap<AccountReadModel, AccountListItemDto>();
        CreateMap<AccountReadModel, AccountDetailDto>();
        CreateMap<Account, CreatedAccountDto>();
    }
}
