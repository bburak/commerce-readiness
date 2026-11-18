using System.Reflection;
using System.Text.RegularExpressions;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation;
using CommerceReadiness.Core.Shopping;
using CommerceReadiness.Core.Storage;
using CommerceReadiness.Shopper;
using CommerceReadiness.Shopper.Agent;
using CommerceReadiness.Shopper.Tools;
using Microsoft.Extensions.AI;

namespace CommerceReadiness.Tests;

public class ShopperTrustBoundaryTests
{
    [Fact]
    public void Production_shopper_types_expose_no_evaluation_or_oracle_types()
    {
        var production = typeof(CommerceTools).Assembly.GetTypes()
            .Where(t => t.IsVisible
                        && t.Namespace is not null
                        && !t.Namespace.Contains("Evaluation", StringComparison.Ordinal)
                        && t != typeof(ShopperCli)
                        && t.Name != nameof(ShopperBenchmarkFile))
            .ToList();

        var offenders = production
            .SelectMany(t => ExposedTypes(t).Select(exposed => $"{t.Name} exposes {exposed.FullName}"))
            .Where(x => x.Contains("CommerceReadiness.Core.Evaluation", StringComparison.Ordinal)
                        || x.Contains("OracleShopper", StringComparison.Ordinal)
                        || x.Contains("IntentExpectation", StringComparison.Ordinal)
                        || x.Contains("AuthoritativeFacts", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.Contains(typeof(CommerceTools), production);
        Assert.Contains(typeof(ShopperAgent), production);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Production_shopper_source_has_no_forbidden_dependencies()
    {
        var forbidden = new Regex(
            "\"clean\"|CleanFolder|\"eval\"|EvalFiles|ground-truth|intents\\.json" +
            @"|using CommerceReadiness\.Core\.Evaluation" +
            @"|\b(GroundTruth|InjectedDefect|AuthoritativeFacts|OracleShopper|IntentExpectation|ShopperEvaluator)\b");
        var root = Path.Combine(TestData.RepoRoot(), "src", "CommerceReadiness.Shopper");
        var allow = new HashSet<string>(StringComparer.Ordinal)
        {
            "ShopperCli.cs", // benchmark composition root loads clean/eval intentionally
        };
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !allow.Contains(Path.GetFileName(f)))
            .ToList();

        Assert.NotEmpty(files);
        var offenders = files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(x => forbidden.IsMatch(x.Text))
            .Select(x => $"{x.File}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Core_still_has_no_ai_or_database_package_dependencies()
    {
        var references = typeof(ShopperResult).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
        Assert.DoesNotContain(references, n =>
            n.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
            || n.StartsWith("OpenAI", StringComparison.Ordinal)
            || n.StartsWith("Npgsql", StringComparison.Ordinal)
            || n.StartsWith("Pgvector", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ask_works_from_the_published_folder_alone()
    {
        var full = TestData.NewSeed42DatasetDirectory();
        var publishedOnly = TestData.NewTempDirectory();
        TestData.CopyDirectory(Path.Combine(full, DatasetLayout.PublishedFolder), Path.Combine(publishedOnly, DatasetLayout.PublishedFolder));

        using var chat = ShopperChatPipeline.Wrap(
            new ScriptedChatClient(ScriptedChatClient.Final(ShopperTestSupport.Clarify("Numara?", ShopperMissingField.ShoeSize))),
            ShopperTestSupport.Settings);
        var (code, output, error) = await ShopperTestSupport.RunCliAsync(
            chat, "ask", "--dataset", publishedOnly, "--query", "beyaz ayakkabı", "--embeddings", "hashing", "--index", "memory");

        Assert.Equal(0, code);
        Assert.Empty(error);
        Assert.Contains("NeedsClarification", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Poisoned_clean_and_eval_do_not_change_ask_tool_outputs()
    {
        var baseline = TestData.NewSeed42DatasetDirectory();
        var poisoned = TestData.NewTempDirectory();
        TestData.CopyDirectory(Path.Combine(baseline, DatasetLayout.PublishedFolder), Path.Combine(poisoned, DatasetLayout.PublishedFolder));
        foreach (var folder in new[] { DatasetLayout.CleanFolder, EvalFiles.Folder })
        {
            var directory = Directory.CreateDirectory(Path.Combine(poisoned, folder)).FullName;
            File.WriteAllText(Path.Combine(directory, "store.json"), "{ broken");
            File.WriteAllText(Path.Combine(directory, "intents.json"), "{ broken");
        }

        await using var host = await ShopperTestSupport.OpenMemoryAsync(poisoned);
        var result = host.Tools.GetReturnPolicy(new("SKU-SE-009", nameof(ReturnQuestion.OpenedReturn)));
        Assert.True(result.Ok);
        Assert.Equal(ReturnAnswer.Unspecified, result.Data!.Answer);
    }

    [Fact]
    public async Task OpenAI_shopper_fails_clearly_without_an_api_key()
    {
        var retrieval = ShopperTestSupport.Retrieval with { OpenAiApiKey = null };
        var dataset = TestData.NewSeed42DatasetDirectory();
        var (code, _, error) = await ShopperTestSupport.RunCliAsync(
            chat: null, retrieval, ShopperTestSupport.Settings,
            "ask", "--dataset", dataset, "--query", "çanta", "--embeddings", "hashing", "--index", "memory");

        Assert.Equal(2, code);
        Assert.Contains("OPENAI_API_KEY", error, StringComparison.Ordinal);
    }

    private static IEnumerable<Type> ExposedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetMethods(flags).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Concat(type.GetConstructors(flags).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .Concat(type.GetProperties(flags).Select(p => p.PropertyType))
            .Concat(type.GetFields(flags).Select(f => f.FieldType))
            .SelectMany(Expand);
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Expand(element))
            {
                yield return inner;
            }
        }

        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
        {
            foreach (var inner in Expand(argument))
            {
                yield return inner;
            }
        }
    }
}
