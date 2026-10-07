using Monitoring.Domain.Security;

namespace Monitoring.Tests;

public sealed class AccessMatrixTests
{
    [Theory]
    // Sessions, detail and the confirmed inventory: all three roles.
    [InlineData("analista", Operation.ReadSessions, true)]
    [InlineData("auditor", Operation.ReadSessions, true)]
    [InlineData("administrador-inventario", Operation.ReadSessions, true)]
    [InlineData("analista", Operation.ReadInventory, true)]
    [InlineData("auditor", Operation.ReadInventory, true)]
    [InlineData("administrador-inventario", Operation.ReadInventory, true)]
    // Candidates and observations: analyst and administrator only; the auditor is denied.
    [InlineData("analista", Operation.ReadCandidatesAndObservations, true)]
    [InlineData("auditor", Operation.ReadCandidatesAndObservations, false)]
    [InlineData("administrador-inventario", Operation.ReadCandidatesAndObservations, true)]
    // Create, correct, confirm, reject and merge: the administrator only.
    [InlineData("analista", Operation.ManageInventory, false)]
    [InlineData("auditor", Operation.ManageInventory, false)]
    [InlineData("administrador-inventario", Operation.ManageInventory, true)]
    public void EachCellOfTheMatrixIsExactlyAsSpecified(string role, Operation operation, bool expected) =>
        Assert.Equal(expected, RoleMatrix.Permits([role], operation));

    [Fact]
    public void SeveralRolesGrantOnlyTheUnionOfWhatEachDeclares()
    {
        Assert.True(RoleMatrix.Permits(["auditor", "analista"], Operation.ReadCandidatesAndObservations));
        Assert.False(RoleMatrix.Permits(["auditor", "analista"], Operation.ManageInventory));
        Assert.True(RoleMatrix.Permits(["auditor", "administrador-inventario"], Operation.ManageInventory));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("realm-admin")]
    [InlineData("ANALISTA")]
    [InlineData("")]
    public void AnUnknownOrDifferentlyCasedRoleGrantsNothing(string role)
    {
        foreach (var operation in Enum.GetValues<Operation>()) Assert.False(RoleMatrix.Permits([role], operation));
    }

    [Fact]
    public void NoRolesMeansNoPermissionAndEveryOperationIsCoveredByTheMatrix()
    {
        foreach (var operation in Enum.GetValues<Operation>())
        {
            Assert.False(RoleMatrix.Permits([], operation));
            // The administrator is the superset: a new operation that nobody grants would show up here.
            Assert.True(RoleMatrix.Permits([Roles.InventoryAdministrator], operation));
        }
    }
}
