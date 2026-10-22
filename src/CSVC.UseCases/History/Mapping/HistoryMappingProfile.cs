using AutoMapper;
using CSVC.UseCases.History.DTOs;
using CSVC.UseCases.History.Models;

namespace CSVC.UseCases.History.Mapping;

public sealed class HistoryMappingProfile : Profile
{
    public HistoryMappingProfile()
    {
        CreateMap<AccountHistoryReadModel, AccountHistoryListItemDto>();
        CreateMap<AccountHistoryReadModel, AccountHistoryDetailDto>();
        CreateMap<AuthHistoryReadModel, AuthHistoryListItemDto>();
        CreateMap<AuthHistoryReadModel, AuthHistoryDetailDto>()
            .ForMember(d => d.IpAddress, o => o.MapFrom(s => s.IpAddress == null ? null : s.IpAddress.ToString()));
        CreateMap<DepartmentHistoryReadModel, DepartmentHistoryListItemDto>();
        CreateMap<DepartmentHistoryReadModel, DepartmentHistoryDetailDto>();
    }
}
