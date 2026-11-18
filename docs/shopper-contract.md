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
| `search_products` | query + hard filters, `top` (default 5, max 10) | always Hybrid via `CommerceReadiness.Retrieval.Runtime` | Price (from Pricing-backed search docs); discovery only |
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

Grounding is separate from outcome correctness:

- A grounded recommendation needs a clean-valid primary SKU, every mandatory constraint supported by the right tool
  evidence, and no contradicted claim. Zero claims means zero grounding credit.
- A grounded NoMatch needs tool evidence for at least one ground-truth decisive blocking constraint. It does **not**
  claim that the trace proves no catalog SKU can match.
- Correct clarification requires `NeedsClarification` and every expected `ShopperMissingField` from
  `ConstraintEvaluator.MissingInformation`.

## Prompt and model

- Prompt version: `shopper-prompt-v1` (embedded resource)
- Default model: `Shopper__Model=gpt-6.2-sol` (recorded in every run; never silently downgraded)
- OpenAI Responses API via `ResponsesClient.AsIChatClient`. The adapter is marked experimental (`OPENAI001`); the
  diagnostic is suppressed only in `ShopperChatClientFactory.cs`.

## CLI

```
dotnet run --project src/CommerceReadiness.Shopper -- ask --query "..." --dataset demo-store/generated/seed-42
dotnet run --project src/CommerceReadiness.Shopper -- benchmark --dataset demo-store/generated/seed-42 --runs 3
```

`--show-tools` prints the compact evidence trace. Benchmark writes
`demo-store/generated/seed-42/shopper-runs/benchmark-<model>-shopper-prompt-v1.json`.

## Observed run (seed 42)

`gpt-6.2-sol` × `shopper-prompt-v1` × 3 runs × 20 intents (60 attempted):

- RunCompletionRate 1.000, OutcomeAccuracy 0.950, ConfidentWrongRate 0, UnsupportedClaimRate 0
- CorrectNoMatchRate / GroundedNoMatchRate 1.000 (includes I17 return evidence and I18 delivery evidence)
- CorrectClarificationRate 1.000 (I19 shoe size, I20 delivery city)
- OutcomeStabilityRate 1.000
- Known miss: I07 false abstention on all three runs (conditional opened-return headphones). Not tuned away.
