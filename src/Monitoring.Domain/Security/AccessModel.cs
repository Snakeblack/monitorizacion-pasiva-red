using Monitoring.Domain.Sessions.Search;

namespace Monitoring.Domain.Security;

public static class Roles
{
    public const string Analyst = "analista";
    public const string Auditor = "auditor";
    public const string InventoryAdministrator = "administrador-inventario";

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { Analyst, Auditor, InventoryAdministrator };
}

// Every human operation the API exposes. Anything not listed here has no permission for any role.
public enum Operation
{
    ReadSessions,
    ReadInventory,
    ReadCandidatesAndObservations,
    ManageInventory
}

// Matrix F-07. Deny by default: a role grants only the operations declared for it, several roles grant the union of their own, and a
// role name the realm invents grants nothing.
public static class RoleMatrix
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<Operation>> Grants = new Dictionary<string, IReadOnlySet<Operation>>(StringComparer.Ordinal)
    {
        [Roles.Analyst] = new HashSet<Operation> { Operation.ReadSessions, Operation.ReadInventory, Operation.ReadCandidatesAndObservations },
        [Roles.Auditor] = new HashSet<Operation> { Operation.ReadSessions, Operation.ReadInventory },
        [Roles.InventoryAdministrator] = new HashSet<Operation>
        {
            Operation.ReadSessions, Operation.ReadInventory, Operation.ReadCandidatesAndObservations, Operation.ManageInventory
        }
    };

    public static bool Permits(IEnumerable<string> roles, Operation operation) =>
        roles.Any(role => Grants.TryGetValue(role, out var granted) && granted.Contains(operation));
}

// A person already authenticated by the identity provider. Roles and scopes come only from validated token claims.
public sealed record HumanIdentity(string Subject, IReadOnlySet<string> Roles, IReadOnlyCollection<AuthorizedPair> Scopes);
