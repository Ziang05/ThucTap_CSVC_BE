using AutoMapper;
using CSVC.Core.Common;
using CSVC.Core.Departments;
using CSVC.UseCases.Departments.DTOs;

namespace CSVC.UseCases.Departments.Mapping;

public sealed class DepartmentMappingProfile : Profile
{
    public DepartmentMappingProfile()
    {
        MapWrite<CreateDepartmentRequest>();
        MapWrite<UpdateDepartmentRequest>();
        CreateMap<Department, DepartmentListItemDto>();
        CreateMap<Department, DepartmentDetailDto>().ForMember(d => d.Management, o => o.Ignore());
    }

    private void MapWrite<T>() where T : DepartmentWriteRequest
        => CreateMap<T, Department>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.Code, o => o.Ignore())
            .ForMember(d => d.Status, o => o.Ignore())
            .ForMember(d => d.Name, o => o.MapFrom(s => TextNormalization.Name(s.Name)))
            .ForMember(d => d.ShortName, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.ShortName) ? null : s.ShortName.Trim()))
            .ForMember(d => d.Description, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim()));
}
