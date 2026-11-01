namespace CSVC.Core.Devices;

public enum DeviceCondition
{
    Good,
    Damaged,
    Missing
}

public enum DeviceOperationKind
{
    Receipt,
    Loan,
    Transfer,
    ServiceOrder
}

public enum DeviceCodeSource
{
    Manual,
    Generated
}
