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
    Transfer
}

public enum DeviceCodeSource
{
    Manual,
    Generated
}
