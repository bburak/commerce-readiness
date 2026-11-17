namespace CommerceReadiness.Core.Domain;

/// <summary>Orders placed before <see cref="CutoffHour"/> (Turkey time) on a business day ship the same day.</summary>
public sealed record Warehouse(string WarehouseId, string Name, City City, int CutoffHour);

public sealed record ShippingRule(string WarehouseId, City Destination, int TransitBusinessDays);

/// <summary>SKUs of <see cref="Category"/> priced at or above <see cref="MinimumPriceTry"/> can be paid in up to <see cref="MaxInstallments"/>.</summary>
public sealed record InstallmentRule(Category Category, decimal MinimumPriceTry, int MaxInstallments);

public enum ReturnQuestion
{
    UnopenedReturn,
    OpenedReturn,
    DefectiveReturn,
}

public enum ReturnAnswer
{
    Yes,
    No,
    Conditional,
    Unspecified,
}

/// <summary>One cell of the category x return-question policy matrix.</summary>
public sealed record ReturnRule(
    Category Category,
    ReturnQuestion Question,
    ReturnAnswer Answer,
    string? ConditionTr,
    int? WindowDays)
{
    public string CellId => CellIdFor(Category, Question);

    public static string CellIdFor(Category category, ReturnQuestion question) => $"{category}:{question}";
}
