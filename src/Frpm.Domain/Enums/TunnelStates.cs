namespace Frpm.Domain.Enums;

public enum TunnelDesiredState
{
    Stopped = 0,
    Running = 1
}

public enum TunnelRuntimeState
{
    Unknown = 0,
    Stopped = 1,
    Starting = 2,
    Running = 3,
    Stopping = 4,
    Failed = 5
}

public enum TunnelRemoteState
{
    Unknown = 0,
    Active = 1,
    Inactive = 2,
    RemoteMissing = 3,
    Deleted = 4
}
