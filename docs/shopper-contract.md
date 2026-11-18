# Shopper contract (Milestone 3)

Milestone 3 answers: can an LLM understand a shopper's natural-language request, call commerce tools correctly, and
produce a safe, evidence-backed `ShopperResult`? The model never owns commerce truth and never evaluates itself.

## Production vs evaluation

```
Published store --> Hybrid retrieval + read-only tools --> LLM --> ShopperResult
ShopperResult + ToolInvocationTrace + Clean + intents oracle --> ShopperEvaluator --> metrics
```

Production code (`CommerceTools`, `ShopperAgent`, `ask`) loads only `published/`. It never reads `clean/`, ground
truth, oracle expectations or retrieval expected relevance. The `benchmark` command is the evaluation composition
root: it loads clean and `eval/intents.json` only for scoring.

## Tools (read-only)

| Tool | LLM supplies | Application resolves | Authoritative for |
|---|---|---|---|
| `search_products` | query + hard filters, `top` (model-selected; default 5, max 10) | always Hybrid via `CommerceReadiness.Retrieval.Runtime` | Price (from Pricing-backed search docs); discovery only |
| `get_product_details` | SKUs | catalog / variant attributes, Pricing price | ProductAttribute (no description) |
| `get_inventory` | SKUs | `inventory.json` | Availability |
| `get_delivery_estimate` | SKU, city, optional withinDays | warehouse, shipping, `DeliveryCalculator`, store `asOf` | Delivery |
| `get_return_policy` | SKU, question | category from SKU, published return rule | ReturnPolicy |
| `get_payment_options` | SKU | category and price from SKU, installment rules | PaymentOption |

Principle: user-intent facts may come from the LLM; entity-derived commerce facts are resolved by the application.
Similarity is never evidence for a product attribute. Misleading free-text descriptions are not citable facts.

Every tool return is a `ToolResultEnvelope` with an application-issued `EvidenceCallId` (`EVID-001`, …). Validation
and business failures return typed tool errors; unexpected exceptions fail the run.

## Limits

- `MaximumIterationsPerRequest = 8`
- `Shopper__MaxToolCalls = 20` (application budget; one model turn may contain several calls)
- `MaximumConsecutiveErrorsPerRequest = 3` (function-invocation layer only)
- Concurrent tool invocation is disabled

## ShopperResult

```
ShopperOutcome: Recommended | NoMatch | NeedsClarification

ShopperResult {
  schemaVersion: 1
  outcome
  clarification?: { questionTr, missingFields: ShopperMissingField[] }
  recommendations[]   // [0] is primary; Confirmed required
  noMatchReason?
  supportingEvidenceCallIds[]
}
```

Invariants are enforced by `ShopperResultValidator` after deserialization. Claim values use canonical formats
(`ClaimFormat`): money `0.00`, dates `yyyy-MM-dd`, `InStock`/`OutOfStock`, enum names, `true`/`false`, integers,
return `Answer` or `Answer:windowDays`.

## Evaluation (no LLM judge)

`ShopperEvaluator` maps oracle outcomes to shopper outcomes and scores every **attempted** run. Provider or agent
errors count as failures in primary denominators (`RunCompletionRate`, `OutcomeAccuracy`, …). Rates with a zero
denominator are `null` (n/a), never 0.

Grounding is separate from outcome correctness, and **decision grounding is separate from claim grounding**:

- **Decision / constraint grounding** (`MandatoryConstraintEvidenceCoverage`, part of
  `GroundedRecommendationSuccessRate`): for each recommended SKU and each mandatory constraint, the successful
  `ToolInvocationTrace` must contain authoritative evidence for **that** SKU and context (allowed tool, correct
  attribute field / delivery city / return question / payment entity) whose canonical value satisfies the
  constraint. Emitting a redundant final `GroundedClaim` is **not** required. Trace grounding proves that
  authoritative evidence was obtained and available to support the decision; it does **not** claim access to the
  model's hidden causal reasoning.
- **Claim grounding** (`UnsupportedClaimRate`, `EvidenceCoverage`, malformed/wrong-source/contradicted verdicts):
  only factual claims actually emitted in `ShopperResult`. Every emitted claim must still cite valid supporting
  evidence.
- A grounded recommendation needs a clean-valid primary SKU, every mandatory constraint trace-grounded, no
  contradicted emitted claim, and a well-formed `ShopperResult`.
- A grounded NoMatch needs tool evidence for at least one ground-truth decisive blocking constraint. It does **not**
  claim that the trace proves no catalog SKU can match.
- Correct clarification requires `NeedsClarification` and every expected `ShopperMissingField` from
  `ConstraintEvaluator.MissingInformation`.
- `GroundedDecisionRate` counts trace-grounded valid recommendations, evidence-backed NoMatch, and correct
  missing-field clarifications.

## Prompt and model

- Prompt version: `shopper-prompt-v2` (embedded resource). `shopper-prompt-v1` remains on disk / in the v1
  benchmark file as historical evidence; do not reuse v1 scores as v2 scores.
- Return-policy interpretation (v2): `Yes` / `Conditional` / `No` are factual tool answers. Shoppers who
  explicitly require unconditional return (“koşulsuz”, “şartsız”, “hiçbir koşul olmadan”) need `Yes`. A generic
  “can return after opening / if dissatisfied” request may accept `Conditional`; the published `ConditionTr` and
  window must be communicated, never restated as unconditional. `AllowConditional` stays eval-only.
- Default model: `Shopper__Model=gpt-6.2-sol` (recorded in every run; never silently downgraded)
- OpenAI Responses API via `ResponsesClient.AsIChatClient`. The adapter is marked experimental (`OPENAI001`); the
  diagnostic is suppressed only in `ShopperChatClientFactory.cs`.

## CLI

```
dotnet run --project src/CommerceReadiness.Shopper -- ask --query "..." --dataset demo-store/generated/seed-42
dotnet run --project src/CommerceReadiness.Shopper -- benchmark --dataset demo-store/generated/seed-42 --runs 3
dotnet run --project src/CommerceReadiness.Shopper -- rescore --dataset demo-store/generated/seed-42 \
  --file demo-store/generated/seed-42/shopper-runs/benchmark-<model>-shopper-prompt-v1.json
```

`--show-tools` prints the compact evidence trace. Benchmark writes
`demo-store/generated/seed-42/shopper-runs/benchmark-<model>-shopper-prompt-v2.json` (v1 file kept as history).
`rescore` refreshes the evaluation report and search-top provenance from frozen runs without calling OpenAI.

## Retrieval depth: Milestone 2 vs Milestone 3

- **Milestone 2** retrieval benchmarks measure StructuredOnly / SemanticOnly / Hybrid at a **fixed K=5**.
- **Milestone 3** shopper runs use Hybrid with **`defaultSearchTop=5`**, **`maxSearchTop=10`**, and **Top chosen by
  the model on each `search_products` call**. Requested Top is preserved in the tool trace.
- Do not present M2 and M3 as the same retrieval-depth experiment. Shopper correctness metrics do not assume a
  fixed K; they score outcomes and evidence from whatever Top the agent actually used.

Benchmark file provenance fields: `retrievalMode`, `defaultSearchTop`, `maxSearchTop`, plus diagnostics
`totalSearchCalls` and `searchTopDistribution` computed from frozen traces (not hardcoded into the evaluator).

## Observed run (seed 42)

**v2 (current):** `gpt-6.2-sol` × `shopper-prompt-v2` × 3 × 20 (60 attempted); file
`benchmark-gpt-6.2-sol-shopper-prompt-v2.json`.

- Retrieval: Hybrid; defaultSearchTop 5; maxSearchTop 10; searchTopDistribution **10→70**
- OutcomeAccuracy 0.983 (59/60), GroundedRecommendationSuccessRate 0.976 (41/42), GroundedDecisionRate 0.983
- FalseAbstentionRate 0.024 (1/42) — residual **I07#2** only
- **OutcomeStabilityRate = 19/20** (only I07 unstable across its three runs)
- CorrectNoMatch / GroundedNoMatch / Clarification all 1.000; I17 (“koşulsuz”) remains NoMatch ×3
- ConfidentWrongRate 0, UnsupportedClaimRate 0

**Known limitation (do not tune):** I07#2 falsely abstained because the model could not verify “kablosuz /
wireless” from authoritative structured attributes (no such published field; titles/descriptions are not
claim evidence). Return `Conditional` was already accepted in that run. This is an intentional accepted
gap from missing structured evidence, not a prompt/return-policy defect to chase.

**v1 (historical, unchanged):** `benchmark-gpt-6.2-sol-shopper-prompt-v1.json` — OutcomeAccuracy 0.950 with
stable I07 false abstention ×3 before the general return-policy prompt rule.
