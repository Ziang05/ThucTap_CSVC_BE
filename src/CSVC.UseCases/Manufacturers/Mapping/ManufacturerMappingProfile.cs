using AutoMapper;
using CSVC.Core.Common;
using CSVC.Core.Manufacturers;
using CSVC.UseCases.Manufacturers.DTOs;
using CSVC.UseCases.Manufacturers.Models;

namespace CSVC.UseCases.Manufacturers.Mapping;

public sealed class ManufacturerMappingProfile : Profile
{
    public ManufacturerMappingProfile()
    {
        MapWrite<CreateManufacturerRequest>();
        MapWrite<UpdateManufacturerRequest>();
        CreateMap<Manufacturer, ManufacturerListItemDto>();
        CreateMap<Manufacturer, ManufacturerDetailDto>()
            .ForMember(d => d.Version, o => o.Ignore()).ForMember(d => d.Management, o => o.Ignore());
        CreateMap<ManufacturerHistoryReadModel, ManufacturerHistoryListItemDto>();
        CreateMap<ManufacturerHistoryDetailReadModel, ManufacturerHistoryDetailDto>()
            .ForMember(d => d.RequestId, o => o.Ignore()).ForMember(d => d.ReasonCode, o => o.Ignore())
            .ForMember(d => d.ChangedFields, o => o.Ignore()).ForMember(d => d.BeforeData, o => o.Ignore())
            .ForMember(d => d.AfterData, o => o.Ignore());
    }

    private void MapWrite<T>() where T : ManufacturerWriteRequest
        => CreateMap<T, Manufacturer>()
            .ForMember(d => d.Id, o => o.Ignore()).ForMember(d => d.DisplayCode, o => o.Ignore())
            .ForMember(d => d.Status, o => o.Ignore())
            .ForMember(d => d.Name, o => o.MapFrom(s => TextNormalization.Name(s.Name)))
            .ForMember(d => d.Description, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim()))
            .ForMember(d => d.Notes, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim()));
}
