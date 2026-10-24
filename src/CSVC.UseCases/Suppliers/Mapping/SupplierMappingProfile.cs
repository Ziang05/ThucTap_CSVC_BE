using AutoMapper;
using CSVC.Core.Common;
using CSVC.Core.Suppliers;
using CSVC.UseCases.Suppliers.DTOs;
using CSVC.UseCases.Suppliers.Models;

namespace CSVC.UseCases.Suppliers.Mapping;

public sealed class SupplierMappingProfile : Profile
{
    public SupplierMappingProfile()
    {
        MapWrite<CreateSupplierRequest>();
        MapWrite<UpdateSupplierRequest>();
        CreateMap<Supplier, SupplierListItemDto>();
        CreateMap<Supplier, SupplierDetailDto>()
            .ForMember(d => d.Version, o => o.Ignore()).ForMember(d => d.Management, o => o.Ignore());
        CreateMap<SupplierHistoryReadModel, SupplierHistoryListItemDto>();
        CreateMap<SupplierHistoryDetailReadModel, SupplierHistoryDetailDto>()
            .ForMember(d => d.RequestId, o => o.Ignore()).ForMember(d => d.ReasonCode, o => o.Ignore())
            .ForMember(d => d.ChangedFields, o => o.Ignore()).ForMember(d => d.BeforeData, o => o.Ignore())
            .ForMember(d => d.AfterData, o => o.Ignore());
    }

    private void MapWrite<T>() where T : SupplierWriteRequest
        => CreateMap<T, Supplier>()
            .ForMember(d => d.Id, o => o.Ignore()).ForMember(d => d.DisplayCode, o => o.Ignore())
            .ForMember(d => d.Status, o => o.Ignore())
            .ForMember(d => d.Name, o => o.MapFrom(s => TextNormalization.Name(s.Name)))
            .ForMember(d => d.Description, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Description) ? null : s.Description.Trim()))
            .ForMember(d => d.Address, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Address) ? null : s.Address.Trim()))
            .ForMember(d => d.Notes, o => o.MapFrom(s => string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim()));
}
