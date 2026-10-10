using AiCad.Contracts;
using Xunit;

namespace AiCad.Contracts.Tests;

public sealed class OperationPolicyTests
{
    [Theory]
    [InlineData(OperationKind.Create, false)]
    [InlineData(OperationKind.Modify, true)]
    [InlineData(OperationKind.Delete, true)]
    [InlineData(OperationKind.Mixed, true)]
    public void RequiresApproval_FollowsActualOperation(OperationKind operation, bool expected)
    {
        var plan = PlanSamples.WithOperation(operation);

        Assert.Equal(expected, OperationPolicy.RequiresApproval(plan));
    }

    [Fact]
    public void RequiresApproval_DoesNotTrustDeclaredCreate()
    {
        var plan = PlanSamples.WithOperation(OperationKind.Modify) with
        {
            DeclaredOperation = OperationKind.Create
        };

        Assert.True(OperationPolicy.RequiresApproval(plan));
    }
}
