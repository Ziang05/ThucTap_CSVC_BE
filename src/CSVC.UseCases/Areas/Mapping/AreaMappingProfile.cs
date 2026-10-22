using AutoMapper;
using CSVC.Core.Areas;
using CSVC.Core.Common;
using CSVC.UseCases.Areas.DTOs;
using CSVC.UseCases.Areas.Models;

namespace CSVC.UseCases.Areas.Mapping;

public sealed class AreaMappingProfile : Profile
{
    public AreaMappingProfile()
    {
        MapWrite<CreateAreaRequest>();
        MapWrite<UpdateAreaRequest>();
        CreateMap<AreaReadModel, AreaListItemDto>()
            .ForMember(d => d.Status, o => o.MapFrom(s => s.IsLocked ? EntityStatus.Locked : EntityStatus.Active))
            .ForMember(d => d.EffectiveStatus, o => o.MapFrom(s => s.IsEffectivelyLocked ? EntityStatus.Locked : EntityStatus.Active));
        CreateMap<AreaReadModel, AreaDetailDto>().IncludeBase<AreaReadModel, AreaListItemDto>()
            .ForMember(d => d.Children, o => o.Ignore()).ForMember(d => d.Management, o => o.Ignore());
    }

    private void MapWrite<T>() where T : AreaWriteRequest
        => CreateMap<T, Area>()
            .ForMember(d => d.Id, o => o.Ignore()).ForMember(d => d.Code, o => o.Ignore())
            .ForMember(d => d.Status, o => o.Ignore()).ForMember(d => d.ParentId, o => o.Ignore())
            .ForMember(d => d.Name, o => o.MapFrom(s => TextNormalization.Name(s.Name)))
            .ForMember(d => d.TypeLabel, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.TypeLabel) ? null : s.TypeLabel.Trim()))
            .ForMember(d => d.Description, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim()));
}
