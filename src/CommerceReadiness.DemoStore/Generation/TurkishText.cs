using CommerceReadiness.Core.Catalog;

namespace CommerceReadiness.DemoStore.Generation;

/// <summary>Turkish display formatting built by hand so output never depends on the machine's culture data.</summary>
public static class TurkishText
{
    private static readonly NumberFormatInfo Format = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    /// <summary>4000 becomes "4.000", 1299.90 becomes "1.299,90".</summary>
    public static string Money(decimal value) =>
        value.ToString(value == decimal.Truncate(value) ? "#,0" : "#,0.00", Format);

    /// <summary>Invariant "15.6" becomes "15,6".</summary>
    public static string Decimal(string invariant) => invariant.Replace('.', ',');
}

/// <summary>Templated description phrases. Each phrase asserts exactly one attribute value.</summary>
public static class DescriptionPhrases
{
    public static string For(string key, string value) => key switch
    {
        AttributeKeys.WaterResistance => value switch
        {
            "None" => "Su geçirmezlik özelliği bulunmaz.",
            "WaterRepellent" => "Su itici kumaş hafif yağmurda içeriği korur.",
            "Waterproof" => "Tamamen su geçirmez yapı, sağanak yağmurda bile koruma sağlar.",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        },
        AttributeKeys.Material => $"Malzeme: {value}.",
        AttributeKeys.LaptopSizeIn => $"{TurkishText.Decimal(value)} inç dizüstü bilgisayar bölmesi.",
        AttributeKeys.CapacityLiters => $"{value} litre iç hacim.",
        AttributeKeys.TargetGroup => value == "Unisex" ? "Unisex kalıp." : $"{value} kalıbı.",
        AttributeKeys.BatteryHours => $"Tek şarjla {value} saate kadar kullanım.",
        AttributeKeys.NoiseCancellation => value == "true"
            ? "Aktif gürültü engelleme (ANC) özelliğine sahiptir."
            : "Aktif gürültü engelleme özelliği yoktur.",
        AttributeKeys.Microphone => value == "true" ? "Dahili mikrofon ile net görüşme." : "Mikrofon bulunmaz.",
        AttributeKeys.Connectivity => $"{value} bağlantı.",
        AttributeKeys.WarrantyMonths => $"{value} ay resmi distribütör garantisi.",
        AttributeKeys.PowerSource => $"Güç kaynağı: {value}.",
        AttributeKeys.Model => $"Model kodu: {value}.",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}

public sealed record SneakerColor(string Code, string Name, string Lower);

public sealed record SmallElectronicsType(string Name, string Lower, int MinPrice, int MaxPrice, Func<DeterministicRandom, string> PowerSource);

/// <summary>Fictional Turkish brand, model and copy pools.</summary>
public static class CatalogTemplates
{
    public static readonly string[] BackpackBrands = ["Rotaşa", "Kuzey Outdoor", "Metropol", "Patika", "Atlas Gear", "Lodos"];
    public static readonly string[] BackpackModels = ["Urban", "Trek", "Daily", "Commuter", "Explorer", "Nomad", "Pro", "Lite"];
    public static readonly string[] BackpackMaterials = ["Polyester", "Naylon", "Kanvas", "Geri dönüştürülmüş PET", "Cordura"];
    public static readonly string[] LaptopSizes = ["13", "14", "15.6", "16", "17"];

    public static readonly string[] BackpackIntros =
    [
        "Şehir içi kullanım ve kısa seyahatler için tasarlanmış ergonomik sırt çantası.",
        "Hafif yapısı ve dolgulu askılarıyla gün boyu konfor sunar.",
        "Çok bölmeli iç düzeniyle eşyalarınızı kolayca organize edin.",
        "İşe gidiş-dönüş ve kampüs kullanımı için ideal.",
    ];

    public static readonly string[] SneakerBrands = ["Adım", "Koşu Lab", "Pista", "Velo Sport", "Yol Arkadaşı"];
    public static readonly string[] SneakerModels = ["Urban Runner", "Cloud Step", "Track Pro", "City Walk", "Trail Max", "Breeze", "Sprint", "Comfort Plus", "Aero", "Flex"];
    public static readonly string[] SneakerMaterials = ["Mesh", "Deri", "Süet", "Sentetik", "Örgü kumaş"];
    public static readonly string[] TargetGroups = ["Kadın", "Erkek", "Unisex"];

    public static readonly SneakerColor[] SneakerColors =
    [
        new("BLK", "Siyah", "siyah"),
        new("WHT", "Beyaz", "beyaz"),
        new("GRY", "Gri", "gri"),
        new("NVY", "Lacivert", "lacivert"),
        new("RED", "Kırmızı", "kırmızı"),
        new("GRN", "Yeşil", "yeşil"),
    ];

    public static readonly string[] SneakerIntros =
    [
        "Gün boyu konfor sağlayan hafif taban yapısı.",
        "Şehirde yürüyüş ve hafif koşular için tasarlandı.",
        "Nefes alan iç astar ve esnek dış taban.",
    ];

    public static (int Min, int Max) SizeRange(string targetGroup) => targetGroup switch
    {
        "Kadın" => (36, 41),
        "Erkek" => (40, 45),
        _ => (37, 45),
    };

    public static string LowerTargetGroup(string targetGroup) => targetGroup switch
    {
        "Kadın" => "kadın",
        "Erkek" => "erkek",
        _ => "unisex",
    };

    public static readonly string[] HeadphoneBrands = ["Sonik", "Akustika", "Dalga Audio", "Nota", "Frekans"];
    public static readonly string[] HeadphoneModels = ["Air", "Studio", "Pulse", "Echo", "Wave", "Bass", "Free", "Max"];
    public static readonly string[] HeadphoneForms = ["Kulak Üstü", "Kulak İçi"];
    public static readonly string[] Connectivity = ["Bluetooth 5.0", "Bluetooth 5.2", "Bluetooth 5.3"];

    public static readonly string[] HeadphoneIntros =
    [
        "Dengeli ses profili ve uzun süreli kullanım konforu.",
        "Katlanabilir tasarımıyla seyahatlerde kolayca taşınır.",
        "Derin baslar ve net tizler sunan dinamik sürücüler.",
    ];

    public static readonly string[] SmallElectronicsBrands = ["Voltaj", "Pratik", "Nova Elektronik", "Kıvılcım", "Mikro"];
    public static readonly string[] ModelPrefixes = ["VX", "PR", "NX", "KV", "MK"];
    public static readonly int[] WarrantyOptions = [24, 24, 36];

    public static readonly SmallElectronicsType[] SmallElectronicsTypes =
    [
        new("Powerbank", "powerbank", 500, 1600, r => $"Dahili Li-ion pil, {r.Pick(new[] { 10000, 20000 })} mAh"),
        new("Bluetooth Hoparlör", "Bluetooth hoparlör", 800, 3000, r => $"Şarj edilebilir pil, {r.Next(8, 25)} saat kullanım"),
        new("Akıllı Bileklik", "akıllı bileklik", 900, 2600, r => $"Şarj edilebilir pil, {r.Next(5, 15)} gün kullanım"),
        new("Elektrikli Diş Fırçası", "elektrikli diş fırçası", 700, 2200, _ => "Şarj edilebilir pil, USB-C ile şarj"),
        new("Saç Kurutma Makinesi", "saç kurutma makinesi", 1200, 6500, r => $"Şebeke elektriği, {r.Pick(new[] { 1800, 2000, 2200, 2400 })} W"),
    ];

    public static readonly string[] SmallElectronicsIntros =
    [
        "Günlük hayatı kolaylaştıran pratik tasarım.",
        "Kompakt gövdesiyle her yere kolayca taşınır.",
        "Sade ve kullanımı kolay arayüz.",
    ];

    public static SmallElectronicsType TypeByName(string name) => SmallElectronicsTypes.First(t => t.Name == name);
}
