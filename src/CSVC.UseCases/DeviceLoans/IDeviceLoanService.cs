using CSVC.UseCases.Common;
using CSVC.UseCases.DeviceLoans.DTOs;

namespace CSVC.UseCases.DeviceLoans;

public interface IDeviceLoanService
{
    Task<PagedResult<DeviceLoanDto>> ListAsync(DeviceLoanListQuery query, CancellationToken cancellationToken);
    Task<DeviceLoanDto> GetAsync(string loanCode, CancellationToken cancellationToken);
    Task<DeviceLoanDto> CreateAsync(CreateDeviceLoanRequest request, CancellationToken cancellationToken);
    Task UpdateAsync(string loanCode, UpdateDeviceLoanRequest request, CancellationToken cancellationToken);
    Task DecideAsync(string loanCode, bool approve, LoanDecisionRequest request, CancellationToken cancellationToken);
    Task HandoverCompleteAsync(string loanCode, LoanHandoverRequest request, CancellationToken cancellationToken);
    Task ReturnAsync(string loanCode, LoanReturnRequest request, CancellationToken cancellationToken);
    Task CancelAsync(string loanCode, CancellationToken cancellationToken);
    Task<PagedResult<DeviceLoanHistoryDto>> HistoryAsync(string loanCode, DeviceLoanHistoryQuery query, CancellationToken cancellationToken);
}
