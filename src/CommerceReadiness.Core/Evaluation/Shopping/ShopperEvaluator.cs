using CommerceReadiness.Core.Delivery;
using CommerceReadiness.Core.Domain;
using CommerceReadiness.Core.Evaluation.Retrieval;
using CommerceReadiness.Core.Retrieval;
using CommerceReadiness.Core.Shopping;

namespace CommerceReadiness.Core.Evaluation.Shopping;

/// <summary>
/// Deterministic shopper evaluation. It reads the hidden clean store, the intents with their oracle expectations,
/// ground truth and the captured tool traces; it never uses a model. Outcome correctness comes from the oracle.
/// Decision grounding checks that authoritative tool evidence for the recommended SKU (or a decisive NoMatch
/// blocker) is present in the trace; claim grounding separately scores only facts emitted in <see cref="ShopperResult"/>.
/// Trace grounding proves evidence was obtained and available to support the decision — not that the model's
/// hidden causal reasoning is known.
/// </summary>
public static class ShopperEvaluator
{
    private const string Unknown = "unknownSku";

    public static ShopperOutcome ExpectedOutcome(IntentOutcome outcome) => outcome switch
    {
        IntentOutcome.ValidProductsExist => ShopperOutcome.Recommended,
        IntentOutcome.NoValidProduct => ShopperOutcome.NoMatch,
        _ => ShopperOutcome.NeedsClarification,
    };

    public static ShopperMissingField? MissingField(string missingInformationKey) => missingInformationKey switch
    {
        "category" => ShopperMissingField.Category,
        "shoeSize" => ShopperMissingField.ShoeSize,
        "deliverTo" => ShopperMissingField.DeliveryCity,
        _ => null,
    };

    public static ShopperEvaluationReport Evaluate(
        StoreSnapshot clean, StoreSnapshot published, IntentsFile intents, GroundTruth groundTruth, IReadOnlyList<ShopperRunRecord> runs)
    {
        if (clean.Kind != StoreKind.Clean)
        {
            throw new ArgumentException("Shopper evaluation needs the clean authoritative store.", nameof(clean));
        }

        var asOf = intents.SimulationClock;
        var facts = new AuthoritativeFacts(clean, asOf);
        var byId = intents.Intents.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var evaluations = runs
            .Select(r => EvaluateRun(byId[r.IntentId], r, clean, facts, asOf))
            .ToImmutableArray();
        var completed = evaluations.Where(e => e.Status == ShopperRunStatus.Completed).ToList();
        var recommendations = completed.SelectMany(e => e.Recommendations.Select(r => (Run: e, Rec: r))).ToList();
        var claims = recommendations.SelectMany(x => x.Rec.Claims.Select((c, i) => (Unit: $"{Unit(x.Run)}:{x.Rec.Sku}:{i}", Claim: c))).ToList();

        ImmutableArray<ShopperMetric> primary =
        [
            ShopperMetric.Rate("RunCompletionRate", "completed runs / attempted runs", true,
                evaluations.Select(e => (Unit(e), e.Status == ShopperRunStatus.Completed))),
            ShopperMetric.Rate("OutcomeAccuracy", "completed runs with the expected outcome / attempted runs", true,
                evaluations.Select(e => (Unit(e), e.OutcomeCorrect))),
            ShopperMetric.Rate("PrimaryRecommendationSuccessRate",
                "valid-product runs whose primary recommendation is valid in clean truth / attempted valid-product runs", true,
                Expected(evaluations, ShopperOutcome.Recommended).Select(e => (Unit(e), PrimaryValid(e)))),
            ShopperMetric.Rate("GroundedRecommendationSuccessRate",
                "valid-product runs whose primary is clean-valid, every mandatory constraint has satisfying authoritative trace evidence, no contradicted emitted claim, and the ShopperResult is well-formed / attempted valid-product runs",
                true, Expected(evaluations, ShopperOutcome.Recommended).Select(e => (Unit(e), e.GroundedRecommendation == true))),
            ShopperMetric.Rate("MandatoryConstraintEvidenceCoverage",
                "trace-grounded (recommendation, mandatory constraint) pairs / all such pairs of completed Recommended runs (decision evidence; does not require an emitted claim)",
                true, recommendations.SelectMany(x => x.Rec.Constraints.Select(c => ($"{Unit(x.Run)}:{x.Rec.Sku}:{c.Constraint}", c.Grounded)))),
            ShopperMetric.Rate("ValidRecommendationRate", "recommended SKUs valid in clean truth / all recommended SKUs", true,
                recommendations.Select(x => ($"{Unit(x.Run)}:{x.Rec.Sku}", x.Rec.ValidInClean))),
            ShopperMetric.Rate("ConfidentWrongRate", "Confirmed recommendations violating at least one mandatory constraint / all Confirmed recommendations", false,
                recommendations.Where(x => x.Rec.Confidence == RecommendationConfidence.Confirmed)
                    .Select(x => ($"{Unit(x.Run)}:{x.Rec.Sku}", !x.Rec.ValidInClean))),
            ShopperMetric.Rate("UnsupportedClaimRate", "well-formed claims without valid supporting evidence / all well-formed claims", false,
                claims.Where(c => c.Claim.Verdict != ClaimVerdict.Malformed).Select(c => (c.Unit, c.Claim.Verdict != ClaimVerdict.Supported))),
            ShopperMetric.Rate("MalformedClaimRate", "claims whose value is not in canonical format / all claims", false,
                claims.Select(c => (c.Unit, c.Claim.Verdict == ClaimVerdict.Malformed))),
            ShopperMetric.Rate("EvidenceCoverage", "claims supported by at least one valid evidence call / all claims", true,
                claims.Select(c => (c.Unit, c.Claim.Verdict == ClaimVerdict.Supported))),
            ShopperMetric.Rate("CorrectNoMatchRate", "no-valid-product runs completed as NoMatch / attempted no-valid-product runs", true,
                Expected(evaluations, ShopperOutcome.NoMatch).Select(e => (Unit(e), e.OutcomeCorrect))),
            ShopperMetric.Rate("GroundedNoMatchRate",
                "no-valid-product runs completed as NoMatch with tool evidence for a decisive blocking constraint / attempted no-valid-product runs",
                true, Expected(evaluations, ShopperOutcome.NoMatch).Select(e => (Unit(e), e.GroundedNoMatch == true))),
            ShopperMetric.Rate("CorrectClarificationRate",
                "insufficient-information runs completed as NeedsClarification asking for every expected missing field / attempted insufficient-information runs",
                true, Expected(evaluations, ShopperOutcome.NeedsClarification).Select(e => (Unit(e), e.ClarificationCorrect == true))),
            ShopperMetric.Rate("FalseAbstentionRate", "valid-product runs completed as NoMatch or NeedsClarification / attempted valid-product runs", false,
                Expected(evaluations, ShopperOutcome.Recommended).Select(e => (Unit(e),
                    e.Status == ShopperRunStatus.Completed && e.ActualOutcome is ShopperOutcome.NoMatch or ShopperOutcome.NeedsClarification))),
            ShopperMetric.Rate("ToolErrorRate", "failed tool calls / all tool calls", false,
                runs.SelectMany(r => r.Run.Trace.Select(t => ($"{r.IntentId}#{r.RunIndex}:{t.EvidenceCallId}", !t.Ok)))),
            ShopperMetric.Rate("GroundedDecisionRate",
                "runs with a trace-grounded valid recommendation, an evidence-backed NoMatch or a correct missing-field clarification / attempted runs",
                true, evaluations.Select(e => (Unit(e), e.GroundedRecommendation == true || e.GroundedNoMatch == true || e.ClarificationCorrect == true))),
        ];

        var stability = Stability(evaluations);
        var diagnostics = new List<ShopperMetric>
        {
            ShopperMetric.Rate("OutcomeAccuracyCompletedOnly", "completed runs with the expected outcome / completed runs", true,
                completed.Select(e => (Unit(e), e.OutcomeCorrect))),
            ShopperMetric.Rate("UncertainRecommendationRate", "Uncertain recommendations / all recommendations", false,
                recommendations.Select(x => ($"{Unit(x.Run)}:{x.Rec.Sku}", x.Rec.Confidence == RecommendationConfidence.Uncertain))),
            ShopperMetric.Rate("ExtraneousClarificationRate", "NeedsClarification runs asking for a field the oracle does not need / NeedsClarification runs", false,
                completed.Where(e => e.ActualOutcome == ShopperOutcome.NeedsClarification).Select(e => (Unit(e), !e.ExtraneousClarificationFields.IsEmpty))),
            ShopperMetric.Rate("OutcomeStabilityRate", "intents whose outcome is identical across all runs / intents", true,
                stability.Select(s => (s.IntentId, s.Stable))),
            ShopperMetric.Rate("PrimaryRecommendationStabilityRate",
                "valid-product intents whose runs all recommend the same primary SKU / valid-product intents", true,
                stability.Where(s => s.ExpectedOutcome == ShopperOutcome.Recommended)
                    .Select(s => (s.IntentId, s.PrimarySkus.Length == s.Outcomes.Length && s.PrimarySkus.Distinct(StringComparer.Ordinal).Count() == 1))),
        };
        foreach (var field in SearchFilterFields)
        {
            diagnostics.Add(ShopperMetric.Rate($"SearchFilter.{field}",
                $"first search_products calls whose {field} equals the hidden hard filter / runs with a search where the hidden {field} is set", true,
                evaluations.SelectMany(e => e.SearchFilters.Where(f => f.Field == field && f.Expected is not null).Select(f => (Unit(e), f.Correct)))));
        }

        diagnostics.Add(ShopperMetric.Rate("SpuriousSearchFilterRate",
            "first search_products calls that set a hard filter the hidden constraints do not have / runs with a search", false,
            evaluations.Where(e => HasSearch(runs, e))
                .Select(e => (Unit(e), e.SearchFilters.Any(f => f.Expected is null && f.Actual is not null)))));

        var verdicts = claims.GroupBy(c => c.Claim.Verdict.ToString())
            .ToImmutableSortedDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return new ShopperEvaluationReport(
            evaluations.Length,
            evaluations.Select(e => e.IntentId).Distinct(StringComparer.Ordinal).Count(),
            primary,
            [.. diagnostics],
            verdicts,
            stability,
            Misleading(groundTruth, published, byId, runs, evaluations),
            Usage(runs),
            evaluations);
    }

    private static readonly string[] SearchFilterFields =
        ["category", "maxPriceTry", "shoeSize", "color", "minimumLaptopSizeIn", "minimumCapacityLiters", "minimumBatteryHours", "minimumWarrantyMonths"];

    private static RunEvaluation EvaluateRun(ShopperIntent intent, ShopperRunRecord record, StoreSnapshot clean, AuthoritativeFacts facts, DateTimeOffset asOf)
    {
        var run = record.Run;
        var expected = ExpectedOutcome(intent.Expected.Outcome);
        var completed = run.Status == ShopperRunStatus.Completed && run.Result is not null;
        var result = completed ? run.Result : null;
        var trace = run.Trace.ToDictionary(t => t.EvidenceCallId, StringComparer.Ordinal);

        var recommendations = result is { Outcome: ShopperOutcome.Recommended }
            ? result.Recommendations.Select((r, i) => EvaluateRecommendation(intent, r, i == 0, trace, facts, asOf)).ToImmutableArray()
            : [];

        bool? groundedRecommendation = expected == ShopperOutcome.Recommended
            ? result is { Outcome: ShopperOutcome.Recommended }
              && ShopperResultValidator.Validate(result).IsEmpty
              && recommendations is [var first, ..]
              && first.ValidInClean && first.AllConstraintsGrounded && !first.AnyContradicted
            : null;
        bool? groundedNoMatch = expected == ShopperOutcome.NoMatch
            ? result?.Outcome == ShopperOutcome.NoMatch && DecisionGrounded(intent.Constraints, run.Trace, clean, facts, asOf)
            : null;

        var expectedFields = intent.Expected.MissingInformation.Select(MissingField).OfType<ShopperMissingField>().ToHashSet();
        var askedFields = result is { Outcome: ShopperOutcome.NeedsClarification, Clarification: { } clarification }
            ? clarification.MissingFields.ToHashSet()
            : [];
        bool? clarificationCorrect = expected == ShopperOutcome.NeedsClarification
            ? result?.Outcome == ShopperOutcome.NeedsClarification && expectedFields.IsSubsetOf(askedFields)
            : null;

        return new RunEvaluation(
            record.IntentId,
            record.RunIndex,
            run.Status,
            expected,
            result?.Outcome,
            result?.Outcome == expected,
            recommendations,
            groundedRecommendation,
            groundedNoMatch,
            clarificationCorrect,
            [.. askedFields.Except(expectedFields).Order()],
            SearchFilters(intent.Constraints, run.Trace),
            run.Trace.Length,
            run.Trace.Count(t => !t.Ok));
    }

    private static RecommendationEvaluation EvaluateRecommendation(
        ShopperIntent intent, Recommendation recommendation, bool isPrimary, IReadOnlyDictionary<string, ToolInvocationTrace> trace,
        AuthoritativeFacts facts, DateTimeOffset asOf)
    {
        var sku = recommendation.Sku;
        var violations = facts.AllSkus.Contains(sku)
            ? ConstraintEvaluator.Evaluate(intent.Constraints, sku, facts, asOf).Select(v => $"{v.Constraint}: required {v.Required}, actual {v.Actual}")
            : [Unknown];
        // Claim grounding (emitted facts only) stays separate from decision/constraint grounding (trace evidence).
        var checks = recommendation.Claims.Select(c => (Claim: c, Verdict: ClaimEvidence.Verify(sku, c, trace))).ToList();
        var grounding = MandatoryConstraints.For(intent.Constraints, asOf)
            .Select(required => ConstraintEvidence.Ground(sku, required, trace));

        return new RecommendationEvaluation(
            sku,
            isPrimary,
            recommendation.Confidence,
            intent.Expected.ValidSkus.Contains(sku),
            [.. violations],
            [.. grounding],
            [.. checks.Select(c => new ClaimCheck(c.Claim.Type, c.Claim.AttributeField, c.Claim.Value, c.Verdict))]);
    }

    /// <summary>
    /// Decision grounding for NoMatch. It does not prove that no catalog SKU matches (retrieval is top-K); it checks
    /// that the trace holds valid tool evidence for at least one ground-truth decisive blocking constraint.
    /// Decisive constraints block the discovery-relevant clean SKUs; when there are none, the hidden hard filters
    /// are the blockers and the evidence is a search applying one of them at the hidden value or stricter.
    /// </summary>
    public static bool DecisionGrounded(
        ShopperConstraints c, ImmutableArray<ToolInvocationTrace> trace, StoreSnapshot clean, AuthoritativeFacts facts, DateTimeOffset asOf)
    {
        var successful = trace.Where(t => t.Ok && t.Data is not null).ToList();
        var relevant = RetrievalRelevanceOracle.RelevantSkus(clean, RetrievalPolicy.DiscoveryProjection(c), asOf).ToHashSet(StringComparer.Ordinal);
        if (relevant.Count > 0)
        {
            var decisive = relevant.SelectMany(s => ConstraintEvaluator.Evaluate(c, s, facts, asOf)).Select(v => v.Constraint).ToHashSet(StringComparer.Ordinal);
            return successful.Any(entry => ShowsViolation(entry, c, relevant, decisive, asOf));
        }

        var hard = RetrievalPolicy.HardFilters(c);
        return successful.Where(t => t.ToolName == ShopperToolNames.SearchProducts)
            .Any(t => AppliesBlockingFilter(ClaimEvidence.Read<SearchProductsArguments>(t.Arguments), hard));
    }

    private static bool ShowsViolation(ToolInvocationTrace entry, ShopperConstraints c, HashSet<string> relevant, HashSet<string> decisive, DateTimeOffset asOf)
    {
        switch (entry.ToolName)
        {
            case ShopperToolNames.GetDeliveryEstimate when decisive.Contains("deliverTo") || decisive.Contains("deliverWithinDays"):
                var delivery = ClaimEvidence.Read<DeliveryEstimateData>(entry.Data!.Value);
                return relevant.Contains(delivery.Sku) && delivery.City == c.DeliverTo
                    && (delivery.EstimatedDeliveryDate is not { } date
                        || (c.DeliverWithinDays is { } days && date > DeliveryCalculator.Deadline(asOf, days)));
            case ShopperToolNames.GetReturnPolicy when decisive.Contains("returnRequirement") && c.ReturnRequirement is { } requirement:
                var policy = ClaimEvidence.Read<ReturnPolicyData>(entry.Data!.Value);
                return relevant.Contains(policy.Sku) && policy.Question == requirement.Question
                    && !ConstraintEvaluator.SatisfiesReturn(
                        new ReturnRule(policy.Category, policy.Question, policy.Answer, policy.ConditionTr, policy.WindowDays), requirement);
            case ShopperToolNames.GetPaymentOptions when decisive.Contains("installmentCount") && c.InstallmentCount is { } installments:
                var payment = ClaimEvidence.Read<PaymentOptionsData>(entry.Data!.Value);
                return relevant.Contains(payment.Sku) && payment.MaxInstallments < installments;
            default:
                return false;
        }
    }

    private static bool AppliesBlockingFilter(SearchProductsArguments args, StructuredFilters hard)
    {
        if (!string.Equals(args.Category, hard.Category?.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var blockers = new bool?[]
        {
            hard.MaxPriceTry is { } maxPrice ? args.MaxPriceTry <= maxPrice : null,
            hard.ShoeSize is { } size ? args.ShoeSize == size : null,
            hard.Color is { } color ? string.Equals(args.Color, color, StringComparison.OrdinalIgnoreCase) : null,
            hard.MinimumLaptopSizeIn is { } laptop ? args.MinimumLaptopSizeIn >= laptop : null,
            hard.MinimumCapacityLiters is { } capacity ? args.MinimumCapacityLiters >= capacity : null,
            hard.MinimumBatteryHours is { } battery ? args.MinimumBatteryHours >= battery : null,
            hard.MinimumWarrantyMonths is { } warranty ? args.MinimumWarrantyMonths >= warranty : null,
        }.OfType<bool>().ToList();
        return blockers.Count == 0 || blockers.Any(applied => applied);
    }

    private static ImmutableArray<SearchFilterCheck> SearchFilters(ShopperConstraints c, ImmutableArray<ToolInvocationTrace> trace)
    {
        if (trace.FirstOrDefault(t => t.ToolName == ShopperToolNames.SearchProducts && t.Ok) is not { } first)
        {
            return [];
        }

        var args = ClaimEvidence.Read<SearchProductsArguments>(first.Arguments);
        var hard = RetrievalPolicy.HardFilters(c);
        var category = Enum.GetNames<Category>().FirstOrDefault(n => string.Equals(n, args.Category, StringComparison.OrdinalIgnoreCase));
        (string Field, string? Expected, string? Actual)[] fields =
        [
            ("category", hard.Category?.ToString(), category ?? args.Category),
            ("maxPriceTry", Num(hard.MaxPriceTry), Num(args.MaxPriceTry)),
            ("shoeSize", Num(hard.ShoeSize), Num(args.ShoeSize)),
            ("color", hard.Color?.ToLowerInvariant(), args.Color?.Trim().ToLowerInvariant()),
            ("minimumLaptopSizeIn", Num(hard.MinimumLaptopSizeIn), Num(args.MinimumLaptopSizeIn)),
            ("minimumCapacityLiters", Num(hard.MinimumCapacityLiters), Num(args.MinimumCapacityLiters)),
            ("minimumBatteryHours", Num(hard.MinimumBatteryHours), Num(args.MinimumBatteryHours)),
            ("minimumWarrantyMonths", Num(hard.MinimumWarrantyMonths), Num(args.MinimumWarrantyMonths)),
        ];
        return [.. fields.Where(f => f.Expected is not null || f.Actual is not null)
            .Select(f => new SearchFilterCheck(f.Field, f.Expected, f.Actual, f.Expected == f.Actual))];
    }

    private static string? Num(decimal? value) => value is { } v ? InvariantFormat.Number(v) : null;

    private static string? Num(int? value) => value is { } v ? InvariantFormat.Number(v) : null;

    private static ImmutableArray<IntentStability> Stability(ImmutableArray<RunEvaluation> evaluations) =>
    [
        .. evaluations.GroupBy(e => e.IntentId, StringComparer.Ordinal).Select(g =>
        {
            var ordered = g.OrderBy(e => e.RunIndex).ToList();
            ImmutableArray<string> outcomes = [.. ordered.Select(e => e.ActualOutcome?.ToString() ?? e.Status.ToString())];
            ImmutableArray<string> primaries = [.. ordered.Select(e => e.Recommendations.FirstOrDefault()?.Sku).OfType<string>()];
            return new IntentStability(g.Key, ordered[0].ExpectedOutcome, outcomes, primaries, outcomes.Distinct(StringComparer.Ordinal).Count() == 1);
        }),
    ];

    private static ImmutableArray<MisleadingDescriptionCase> Misleading(
        GroundTruth groundTruth, StoreSnapshot published, Dictionary<string, ShopperIntent> intents,
        IReadOnlyList<ShopperRunRecord> runs, ImmutableArray<RunEvaluation> evaluations)
    {
        var evaluationByUnit = evaluations.ToDictionary(Unit, StringComparer.Ordinal);
        return
        [
            .. groundTruth.Defects.Where(d => d.Type == DefectType.MisleadingDescription && d.Sku is not null).Select(defect =>
            {
                var sku = defect.Sku!;
                var field = ProductAttributeFields.ForKey(defect.Field);
                var structured = published.Variants.Any(v => v.Sku == sku)
                    ? published.GetVariant(sku).Attributes.Find(defect.Field) ?? published.ProductOf(sku).Attributes.Find(defect.Field)
                    : null;
                var exposed = runs.Where(r => r.Run.Trace.Any(t => t.ToolName == ShopperToolNames.SearchProducts && t.Ok
                    && ClaimEvidence.Read<SearchProductsData>(t.Data!.Value).Candidates.Any(c => c.Sku == sku))).ToList();
                var withDetails = exposed.Where(r => r.Run.Trace.Any(t => t.ToolName == ShopperToolNames.GetProductDetails && t.Ok
                    && ClaimEvidence.Read<ProductDetailsData>(t.Data!.Value).Products.Any(p => p.Sku == sku))).ToList();
                var recommended = exposed
                    .Select(r => (Run: r, Eval: evaluationByUnit[$"{r.IntentId}#{r.RunIndex}"]))
                    .SelectMany(x => x.Eval.Recommendations.Where(rec => rec.Sku == sku).Select(rec => (x.Run, Rec: rec)))
                    .ToList();
                var misleadingClaims = exposed
                    .Where(r => r.Run.Status == ShopperRunStatus.Completed && r.Run.Result is not null)
                    .SelectMany(r => r.Run.Result!.Recommendations.Where(rec => rec.Sku == sku).SelectMany(rec => rec.Claims))
                    .Count(c => c.Type == ClaimType.ProductAttribute && c.AttributeField == field && field is not null
                                && (structured is null || !ClaimFormat.Equivalent(c, structured)));
                return new MisleadingDescriptionCase(
                    defect.DefectId,
                    sku,
                    defect.Field,
                    structured,
                    exposed.Count,
                    withDetails.Count,
                    recommended.Count(x => x.Rec.Confidence == RecommendationConfidence.Confirmed),
                    recommended.Count(x => x.Rec.Confidence == RecommendationConfidence.Confirmed && !x.Rec.ValidInClean),
                    misleadingClaims,
                    withDetails.Count(r => !recommended.Any(x => x.Run == r)));
            }),
        ];
    }

    private static UsageSummary Usage(IReadOnlyList<ShopperRunRecord> runs)
    {
        long? Sum(Func<ShopperUsage, long?> pick) =>
            runs.Any(r => r.Run.Usage is { } u && pick(u) is not null) ? runs.Sum(r => r.Run.Usage is { } u ? pick(u) ?? 0 : 0) : null;

        return new UsageSummary(
            Sum(u => u.InputTokens),
            Sum(u => u.OutputTokens),
            Sum(u => u.TotalTokens),
            runs.Count == 0 ? 0 : Math.Round(runs.Average(r => r.Run.Trace.Length), 2),
            runs.Count == 0 ? 0 : Math.Round(runs.Average(r => r.Run.DurationMs), 0));
    }

    private static IEnumerable<RunEvaluation> Expected(ImmutableArray<RunEvaluation> evaluations, ShopperOutcome outcome) =>
        evaluations.Where(e => e.ExpectedOutcome == outcome);

    private static bool PrimaryValid(RunEvaluation e) =>
        e.Status == ShopperRunStatus.Completed && e.ActualOutcome == ShopperOutcome.Recommended && e.Recommendations is [{ ValidInClean: true }, ..];

    private static bool HasSearch(IReadOnlyList<ShopperRunRecord> runs, RunEvaluation e) =>
        runs.Any(r => r.IntentId == e.IntentId && r.RunIndex == e.RunIndex
                      && r.Run.Trace.Any(t => t.ToolName == ShopperToolNames.SearchProducts && t.Ok));

    private static string Unit(RunEvaluation e) => $"{e.IntentId}#{e.RunIndex}";
}
