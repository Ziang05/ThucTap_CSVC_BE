using AutoMapper;
using CSVC.Core.Common;
using CSVC.Core.DeviceCategories;
using CSVC.UseCases.DeviceCategories.DTOs;
using CSVC.UseCases.DeviceCategories.Models;

namespace CSVC.UseCases.DeviceCategories.Mapping;

public sealed class CategoryMappingProfile : Profile
{
    public CategoryMappingProfile()
    {
        MapWrite<CreateCategoryRequest>();
        MapWrite<UpdateCategoryRequest>();
        CreateMap<DeviceCategory, CategoryListItemDto>();
        CreateMap<DeviceCategory, CategoryDetailDto>()
            .ForMember(d => d.Version, o => o.Ignore()).ForMember(d => d.Management, o => o.Ignore());
        CreateMap<CategoryHistoryReadModel, CategoryHistoryListItemDto>();
        CreateMap<CategoryHistoryDetailReadModel, CategoryHistoryDetailDto>()
            .ForMember(d => d.RequestId, o => o.Ignore()).ForMember(d => d.ReasonCode, o => o.Ignore())
            .ForMember(d => d.ChangedFields, o => o.Ignore()).ForMember(d => d.BeforeData, o => o.Ignore())
            .ForMember(d => d.AfterData, o => o.Ignore());
    }

    private void MapWrite<T>() where T : CategoryWriteRequest
        => CreateMap<T, DeviceCategory>()
            .ForMember(d => d.Id, o => o.Ignore()).ForMember(d => d.DisplayCode, o => o.Ignore())
            .ForMember(d => d.Status, o => o.Ignore())
            .ForMember(d => d.Name, o => o.MapFrom(s => TextNormalization.Name(s.Name)))
            .ForMember(d => d.Description, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim()))
            .ForMember(d => d.Notes, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim()));
}
