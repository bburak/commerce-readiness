using System.Text;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Evaluation.Metrics;
using CommerceReadiness.Core.Serialization;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.SourceOfTruth;

namespace CommerceReadiness.DemoStore.Output;

public static class DatasetWriter
{
    public const string CleanFolder = "clean";
    public const string PublishedFolder = "published";
    public const string EvalFolder = "eval";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(DemoStoreDataset dataset, string outputDirectory)
    {
        foreach (var folder in new[] { CleanFolder, PublishedFolder, EvalFolder })
        {
            var path = Path.Combine(outputDirectory, folder);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            Directory.CreateDirectory(path);
        }

        WriteStore(dataset.Clean, Path.Combine(outputDirectory, CleanFolder));
        WriteStore(dataset.Published, Path.Combine(outputDirectory, PublishedFolder));

        var eval = Path.Combine(outputDirectory, EvalFolder);
        WriteText(Path.Combine(eval, StoreFileNames.GroundTruth), JsonDefaults.Serialize(dataset.GroundTruth));
        WriteText(Path.Combine(eval, StoreFileNames.Intents), JsonDefaults.Serialize(dataset.Intents));
        WriteText(Path.Combine(eval, StoreFileNames.Metrics), JsonDefaults.Serialize(dataset.Metrics));
    }

    public static void WriteStore(StoreSnapshot store, string directory)
    {
        WriteText(Path.Combine(directory, StoreFileNames.Catalog), CatalogCsv.Write(store));
        WriteText(Path.Combine(directory, StoreFileNames.PriceList), JsonDefaults.Serialize(new PriceListFile(DataSource.Pricing, "TRY", store.Prices)));
        WriteText(Path.Combine(directory, StoreFileNames.Inventory), JsonDefaults.Serialize(new InventoryFile(DataSource.Inventory, store.Inventory)));
        WriteText(Path.Combine(directory, StoreFileNames.Shipping), JsonDefaults.Serialize(
            new ShippingFile(DataSource.Shipping, "Monday-Friday; public holidays not modeled", store.Warehouses, store.ShippingRules)));
        WriteText(Path.Combine(directory, StoreFileNames.Payments), JsonDefaults.Serialize(new PaymentsFile(DataSource.Payments, "TRY", store.InstallmentRules)));
        WriteText(Path.Combine(directory, StoreFileNames.Returns), JsonDefaults.Serialize(new ReturnsFile(DataSource.ReturnPolicy, store.ReturnRules)));
    }

    private static void WriteText(string path, string text) => File.WriteAllText(path, text, Utf8NoBom);
}

public static class DatasetReader
{
    public static StoreSnapshot ReadStore(string directory, StoreKind kind, string storeVersion)
    {
        var (products, variants, listings) = CatalogCsv.Read(Read(directory, StoreFileNames.Catalog));
        var prices = JsonDefaults.Deserialize<PriceListFile>(Read(directory, StoreFileNames.PriceList));
        var inventory = JsonDefaults.Deserialize<InventoryFile>(Read(directory, StoreFileNames.Inventory));
        var shipping = JsonDefaults.Deserialize<ShippingFile>(Read(directory, StoreFileNames.Shipping));
        var payments = JsonDefaults.Deserialize<PaymentsFile>(Read(directory, StoreFileNames.Payments));
        var returns = JsonDefaults.Deserialize<ReturnsFile>(Read(directory, StoreFileNames.Returns));

        return new StoreSnapshot(
            kind, storeVersion, products, variants, listings,
            prices.Prices, inventory.Records, shipping.Warehouses, shipping.Rules, payments.InstallmentRules, returns.Rules);
    }

    public static GroundTruth ReadGroundTruth(string evalDirectory) =>
        JsonDefaults.Deserialize<GroundTruth>(Read(evalDirectory, StoreFileNames.GroundTruth));

    public static IntentsFile ReadIntents(string evalDirectory) =>
        JsonDefaults.Deserialize<IntentsFile>(Read(evalDirectory, StoreFileNames.Intents));

    public static MetricsReport ReadMetrics(string evalDirectory) =>
        JsonDefaults.Deserialize<MetricsReport>(Read(evalDirectory, StoreFileNames.Metrics));

    private static string Read(string directory, string file) => File.ReadAllText(Path.Combine(directory, file));
}
