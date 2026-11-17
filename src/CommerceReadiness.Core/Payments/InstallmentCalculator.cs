using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.Core.Payments;

public static class InstallmentCalculator
{
    /// <summary>Highest installment count any matching rule allows; 1 means single payment only.</summary>
    public static int MaxInstallments(IEnumerable<InstallmentRule> rules, Category category, decimal price) =>
        rules.Where(r => r.Category == category && price >= r.MinimumPriceTry)
            .Select(r => r.MaxInstallments)
            .DefaultIfEmpty(1)
            .Max();
}
