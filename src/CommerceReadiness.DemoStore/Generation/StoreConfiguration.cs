using CommerceReadiness.Core.Domain;

namespace CommerceReadiness.DemoStore.Generation;

/// <summary>Fixed, seed-independent shipping, payment and return configuration of the clean store.</summary>
public static class StoreConfiguration
{
    public const string IstanbulWarehouse = "IST-TUZLA";
    public const string IzmirWarehouse = "IZM-GAZIEMIR";

    public static ImmutableArray<Warehouse> Warehouses { get; } =
    [
        new(IstanbulWarehouse, "İstanbul Tuzla Depo", City.Istanbul, 14),
        new(IzmirWarehouse, "İzmir Gaziemir Depo", City.Izmir, 9),
    ];

    public static ImmutableArray<ShippingRule> ShippingRules { get; } =
    [
        new(IstanbulWarehouse, City.Istanbul, 1),
        new(IstanbulWarehouse, City.Ankara, 2),
        new(IstanbulWarehouse, City.Izmir, 2),
        new(IzmirWarehouse, City.Istanbul, 2),
        new(IzmirWarehouse, City.Ankara, 2),
        new(IzmirWarehouse, City.Izmir, 1),
    ];

    public static ImmutableArray<InstallmentRule> InstallmentRules { get; } =
    [
        new(Category.Backpacks, 1000m, 3),
        new(Category.Backpacks, 2500m, 6),
        new(Category.Sneakers, 1500m, 3),
        new(Category.Sneakers, 3000m, 6),
        new(Category.Headphones, 2000m, 6),
        new(Category.Headphones, 4000m, 9),
        new(Category.SmallElectronics, 1500m, 6),
        new(Category.SmallElectronics, 5000m, 12),
    ];

    public static ImmutableArray<ReturnRule> ReturnRules { get; } =
    [
        new(Category.Backpacks, ReturnQuestion.UnopenedReturn, ReturnAnswer.Yes, null, 14),
        new(Category.Backpacks, ReturnQuestion.OpenedReturn, ReturnAnswer.Conditional, "Ürün kullanılmamış ve etiketleri üzerinde olmalıdır.", 14),
        new(Category.Backpacks, ReturnQuestion.DefectiveReturn, ReturnAnswer.Yes, null, 30),
        new(Category.Sneakers, ReturnQuestion.UnopenedReturn, ReturnAnswer.Yes, null, 30),
        new(Category.Sneakers, ReturnQuestion.OpenedReturn, ReturnAnswer.Conditional, "Ayakkabı dış mekânda giyilmemiş olmalıdır.", 30),
        new(Category.Sneakers, ReturnQuestion.DefectiveReturn, ReturnAnswer.Yes, null, 30),
        new(Category.Headphones, ReturnQuestion.UnopenedReturn, ReturnAnswer.Yes, null, 14),
        new(Category.Headphones, ReturnQuestion.OpenedReturn, ReturnAnswer.Conditional, "Hijyen bandı açılmamış ve ürün hasarsız olmalıdır.", 14),
        new(Category.Headphones, ReturnQuestion.DefectiveReturn, ReturnAnswer.Yes, null, 30),
        new(Category.SmallElectronics, ReturnQuestion.UnopenedReturn, ReturnAnswer.Yes, null, 14),
        new(Category.SmallElectronics, ReturnQuestion.OpenedReturn, ReturnAnswer.No, "Ambalajı açılmış elektronik ürünler iade alınmaz.", null),
        new(Category.SmallElectronics, ReturnQuestion.DefectiveReturn, ReturnAnswer.Yes, null, 30),
    ];

    /// <summary>Prices end in 99.90 TL, e.g. 1234 becomes 1299.90.</summary>
    public static decimal RoundPrice(decimal raw) => (Math.Floor(raw / 100m) * 100m) + 99.90m;
}
