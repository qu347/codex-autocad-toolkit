namespace AiCad.Contracts;

public static class OperationPolicy
{
    public static bool RequiresApproval(DrawingPlan plan)
    {
        return PlanValidator.Classify(plan.Steps) != OperationKind.Create;
    }
}
