namespace CommerceReadiness.Core.Domain;

public enum Category
{
    Backpacks,
    Sneakers,
    Headphones,
    SmallElectronics,
}

public enum City
{
    Istanbul,
    Ankara,
    Izmir,
}

public enum StockStatus
{
    InStock,
    OutOfStock,
}

/// <summary>Ordered from weakest to strongest so "at least" comparisons work.</summary>
public enum WaterResistance
{
    None = 0,
    WaterRepellent = 1,
    Waterproof = 2,
}

/// <summary>
/// Clean is the hidden ground truth. Published is the observable store a future shopper can access.
/// Repaired is reserved for the analyzer-driven repair flow of a later milestone.
/// </summary>
public enum StoreKind
{
    Clean,
    Published,
    Repaired,
}
