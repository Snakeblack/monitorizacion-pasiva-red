using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Domain.Inventory;

// The OIDC subject of the person acting and the (site, sensor) scopes that person is currently authorized for. Role checks
// (only an inventory administrator may decide) belong to the caller; scope checks are applied here for every affected scope.
public sealed record InventoryActor(string Subject, IReadOnlyCollection<AuthorizedPair> Scopes);

public enum InventoryStatus { Ok, NotFound, Conflict, Forbidden, Invalid }

public interface IInventoryResult
{
    InventoryStatus Status { get; }
}

public sealed record InventoryResult<T>(InventoryStatus Status, T? Value = default, string? Code = null) : IInventoryResult
{
    public static InventoryResult<T> Success(T value) => new(InventoryStatus.Ok, value);
    public static InventoryResult<T> Failure(InventoryStatus status, string code) => new(status, default, code);
}

public sealed record DeviceView(Guid DeviceId, string Name, string Description, long Revision, IReadOnlyList<AuthorizedPair> Scopes, DateTimeOffset UpdatedAt);

public sealed record IpAssociationView(string Ip, DateTimeOffset FirstSeen, DateTimeOffset LastSeen);

public sealed record CandidateView(Guid CandidateId, string SiteId, string SensorId, string Mac, int? VlanId, DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen, string State, long Revision, Guid? DeviceId, IReadOnlyList<IpAssociationView> Associations);

public sealed record ObservationView(string SiteId, string SensorId, string EventId, DateTimeOffset ObservedAt, string Ip, string? Mac, int? VlanId);

// Next is an opaque keyset position for the following page, or null when the listing is exhausted.
public sealed record InventoryPage<T>(IReadOnlyList<T> Items, string? Next);

public static class InventoryLimits
{
    public const int MaximumNameLength = 128;
    public const int MaximumDescriptionLength = 1024;
    public const int MaximumReasonLength = 256;
    public const int MaximumPageSize = 100;
}
