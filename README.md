# Commerce Readiness Lab

Commerce Readiness Lab tests whether an ecommerce store can be reliably understood and used by AI shopping agents:
can an agent find the right product, respect the shopper's constraints, and avoid confidently wrong answers when the
store's own data is stale, incomplete or contradictory?

This repository is at **Milestone 1**. It contains a deterministic synthetic store (DemoStore) with a trustworthy
evaluation foundation (Milestone 0) and a deterministic Readiness Analyzer that reads only published data, scored by a
separate evaluator (Milestone 1). There is no AI in it yet, on purpose.

## What Milestone 0 contains

- A seeded generator that builds a **clean**, internally consistent store of 100 SKUs (78 products) in four
  categories: Backpacks, Sneakers (8 products, 30 size/color variants), Headphones and SmallElectronics.
- A **published** copy of that store with 24 deterministically injected defects and 8 decoys.
- **Ground truth** for every defect and decoy, plus the semantic facts each product description asserts.
- 20 Turkish **shopper intents** with hidden structured constraints and expected results computed by a
  deterministic **oracle** (14 with valid products, 4 with no valid product, 2 with insufficient information).
- **Readiness metrics** with explicit numerators and denominators, computed for the clean and published stores.
- The **output contract** a future LLM shopper must follow, so its answers can be evaluated deterministically.
## What Milestone 1 adds

- A **Readiness Analyzer** with five explicit rules. It reads only the published store and emits actionable
  **findings**, each with evidence and a suggested fix.
- A separate **AnalyzerEvaluator** that scores findings against ground truth: precision, deterministic recall,
  overall recall and decoy hits.
- An analyzer CLI whose normal mode never touches the clean store or ground truth.
- A `store.json` manifest per store folder (schema version, store version, kind, as-of time).
- 165 xUnit tests in total, including trust-boundary tests.

## Why ground truth comes before AI

An AI shopper can only be evaluated against answers that are known to be correct. If ground truth came from an LLM,
every later score would inherit that model's mistakes. So everything here is computed by plain code from a seed and a
fixed clock: defects are injected by the generator (so they are known exactly), expected results come from the oracle,
and no LLM is used as a judge. Decoys (cases that look broken but are not) make sure a future analyzer cannot score
well simply by flagging everything.

## Clean vs Published

```
seed + fixed clock -> CLEAN STORE (hidden ground truth)
                   -> decoy selection + DEFECT INJECTION
                   -> PUBLISHED STORE (what a future shopper can observe)
                   -> (future) REPAIRED STORE
```

- The **clean store** is hidden ground truth. Only the oracle and the metrics read it.
- The **published store** contains the source-specific data a future shopper can access: catalog, pricing,
  inventory, shipping, payments and returns. The published snapshot as a whole is **not** authoritative; individual
  sources inside it are authoritative for their own domain.
- Defects only touch storefront projections (catalog columns and description text) and the return policy. The
  published pricing, inventory, shipping and payments files stay correct, so every defect is detectable from published
  data alone.
- Both stores share one immutable `StoreSnapshot` shape with a `StoreKind` (`Clean`, `Published`, `Repaired`), so a
  future repair flow is "apply fixes to the published snapshot".

## Source-of-truth rules

| Field | Authoritative source | File | Storefront projection |
|---|---|---|---|
| Product identity, title, description, attributes | Catalog | `catalog.csv` | - |
| Price | Pricing | `price-list.json` | `catalog.csv` `listedPrice` |
| Physical stock | Inventory | `inventory.json` | `catalog.csv` `stockStatus` |
| Delivery promise | Shipping rules + fixed clock | `shipping.json` | `catalog.csv` `deliveryPromiseIstanbul` |
| Return eligibility | Return policy | `returns.json` | - |
| Installments | Payment configuration | `payments.json` | - |

When a projection disagrees with its owner, the owner wins. For example, `catalog.csv` says `IN_STOCK` while
`inventory.json` says `available = 0`: Inventory is right, and this is an `InventoryConflict`. The map is encoded in
`FieldOwnership` and written into `ground-truth.json`.

**Time.** Every time-sensitive calculation uses `SimulationContext.Now`, which defaults to
`2026-11-17T10:00:00+03:00` (a Tuesday). The system clock is never read; a test enforces this. Delivery is
warehouse cutoff + transit business days (Monday to Friday, no holidays) to Istanbul, Ankara or Izmir.

## Defects and decoys

| Defect | Meaning | Decoy (not a defect) |
|---|---|---|
| `MissingRequiredAttribute` | Required structured field is blank | `noiseCancellation=false` is present, not missing |
| `InventoryConflict` | Catalog stock status contradicts inventory | `OUT_OF_STOCK` with `available = 0` |
| `PriceConflict` | Listed price differs from Pricing | Discount vs strike-through price; same price across sneaker colors |
| `MisleadingDescription` | Text asserts a value that contradicts the structured truth | "Su itici kumaş" on a water-repellent bag |
| `AmbiguousReturnRule` | Return-policy cell has no Yes/No/Conditional answer | Explicit `Conditional` with a stated condition |
| `DeliveryConflict` | Promise contradicts the deterministic calculation | Later promise caused by a passed warehouse cutoff |

Every defect records a stable ID (for example `DEF-INV-SKU-BP-004`), its type, the affected product, SKU or policy
cell, the published value, the authoritative value, the source and the expected fix.

## Metric definitions

Each metric is `passed units / applicable units`.

- **Fixed denominators.** The set of applicable units (`EvaluationUnitSet`) is built once from the clean store and
  evaluated against every snapshot, so clean, published and future repaired stores always share denominators.
- **Missing data.** Published data can never add or remove a unit. If the observed data needed to evaluate a unit is
  missing, the unit is `NotEvaluable` and counts as not passing. Deleting data can never improve a score.
- **Traceability.** Every unit has a stable ID. Each defect lists its `expectedImpacts`, the units it is expected to
  make not pass, and one defect can affect several. For example, a stale `OUT_OF_STOCK` row fails its stock unit and
  leaves its delivery unit `NotEvaluable`. Tests prove that the published store's non-passing units are exactly the
  union of these impacts.

1. **Catalog Completeness** = present required **structured** attributes / applicable required structured attributes.
   One unit per clean SKU x category requirement. Description text never counts: a backpack described as "su itici" with
   no structured `waterResistance` is still missing it.
2. **Semantic Coverage** = required attributes present structurally **or** supported by explicit text evidence /
   applicable required attributes. Text evidence counts only when it asserts the authoritative clean-store value, so a
   misleading description can never raise coverage. In Milestone 0, text evidence comes from the templated semantic
   facts in ground truth, not from extraction.

   Evidence is scoped:
   - A product-scoped fact ("Malzeme: Deri.") evidences product-level attributes for every SKU of the product.
   - Variant-level attributes need a SKU-scoped fact. For example, "43 numara siyah seçenek mevcut." becomes
     `shoeSize` and `color` facts for that one SKU.
   - A variant fact without a resolved SKU never counts, so evidence cannot spread to sibling variants.
3. **Data Consistency** = passed consistency checks / applicable consistency checks. The checks are:
   - catalog stock vs inventory (one per SKU);
   - catalog price vs pricing (one per SKU);
   - Istanbul delivery promise vs calculation (one per SKU with authoritative stock; no published promise means
     `NotEvaluable`);
   - description vs authoritative attribute (one per product x describable attribute, and one per sneaker SKU x
     variant attribute; making no claim passes, and an added false claim fails);
   - sneaker variant relationship (one per variant);
   - explicit, authoritative return-policy cell (one per cell).

   This deliberately counts checks rather than "good SKUs / total SKUs". Structured attribute values are not compared
   because Milestone 0 never injects wrong structured values, only missing ones.

Required attributes per category live in `CategoryRequirements` and drive all three metrics.

Seed 42 results:

| Store | Catalog Completeness | Semantic Coverage | Data Consistency |
|---|---|---|---|
| Clean | 675 / 675 (100%) | 675 / 675 (100%) | 653 / 653 (100%) |
| Published | 669 / 675 (99.11%) | 672 / 675 (99.56%) | 633 / 653 (96.94%) |

Defects and metric failures are counted separately. All 24 unique injected defects have an expected impact, and all
24 are detected by at least one non-passing unit. The non-passing units are:

- Catalog Completeness: 6
- Semantic Coverage: 3, from the missing attributes that have no supporting text
- Data Consistency: 20, made up of 18 failed units and 2 `NotEvaluable` delivery units caused by stale
  `OUT_OF_STOCK` rows

`metrics.json` reports these counts as `publishedDefectCoverage`.

## Shopper intents and the oracle

Each intent has an `id`, a `persona`, Turkish `text`, hidden `constraints` and an oracle-generated `expected` block:

```json
{
  "id": "I01",
  "persona": "UrgentDelivery",
  "text": "İstanbul'a iki gün içinde gelecek, ... bir sırt çantası arıyorum.",
  "constraints": { "category": "Backpacks", "maxPriceTry": 3500, "deliverTo": "Istanbul", "deliverWithinDays": 2, ... },
  "expected": { "outcome": "ValidProductsExist", "validSkus": [...], "forbidden": [...], "missingInformation": [], ... }
}
```

- A future shopper sees only `text`; the oracle sees only `constraints`.
- `OracleShopper` reads only the clean store and throws if given anything else. It returns an explicit
  `IntentOutcome` (`ValidProductsExist`, `NoValidProduct` or `InsufficientInformation`), the valid SKUs, the forbidden
  SKUs in the category with the constraints each violates, and any missing information.
- Valid-product intents are anchored on a seeded in-stock SKU, so they stay valid for any seed. No-valid intents ask
  for combinations the generator never produces, such as size 47 or next-day delivery to Ankara. Generation fails if
  the oracle's outcome differs from the intended one.
- `forbidden[].looksValidInPublished` marks traps: SKUs that broken published data makes look valid. Intent I02 always
  contains one, a SKU the catalog still shows as `IN_STOCK` although inventory holds 0 units.
- For `NoValidProduct` and `InsufficientInformation`, the correct shopper behavior is to abstain or ask, not to bend a
  constraint.

## Future shopper contract and evaluation definitions

Defined in `CommerceReadiness.Core.Evaluation.ShopperContract`. Nothing produces this output yet.

```
ShopperResult  { IntentId, Recommendations[], Abstained, MissingInformation[] }
Recommendation { Sku, Claims[], Uncertain }
Claim          { Type (InStock, DeliveryBy, Price, InstallmentCount, ReturnEligible, ...), Value, EvidenceCallId }
```

- **Unsupported claim**: a claim with no evidence tool call, or one that contradicts the evidence it cites.
- **Confident wrong recommendation**: a recommended SKU that violates at least one mandatory constraint and is not
  marked `Uncertain`.
- **Abstention**: no recommendation because no valid answer or sufficient information exists.

See [docs/evaluation-contract.md](docs/evaluation-contract.md) for details.

## Readiness Analyzer (Milestone 1)

The analyzer is the first product-analysis layer. It answers one question: what is wrong with this store's
observable data, and how should it be fixed? Its output is a list of findings.

```
Published Store
      |
      v
Readiness Analyzer ---> Findings ----------+
                                           |
                     Ground Truth -------> Evaluator ---> precision / recall / false positives / false negatives
                     (hidden; read only with --evaluate)
```

### Trust boundary

The analyzer only ever sees the published store:

- the published catalog;
- the authoritative Pricing, Inventory, Shipping, Return Policy and Payments files;
- `CategoryRequirements`;
- the store's as-of time from `published/store.json`.

It never reads the clean store, ground truth, injected defects or oracle results. This is enforced in four ways:

- **API shape.** `ReadinessAnalyzer.Analyze(StoreSnapshot published, DateTimeOffset asOf)` has no ground-truth
  parameter and rejects a clean snapshot.
- **Loader.** `PublishedDatasetLoader` only builds paths under `published/`. It rejects stores declared `Clean`,
  missing manifests and unsupported schema versions.
- **Namespace direction.** `Core.Analysis` never references `Core.Evaluation` or `Core.Shopping`; the evaluator lives in
  `Core.Evaluation` and consumes analyzer output.
- **Tests.** The primary proof is behavioral:
  - a dataset containing only `published/` analyzes to byte-identical findings;
  - a dataset whose `clean/` and `eval/` folders contain garbage still analyzes normally.

  A reflection test over the public analyzer API and a narrow source scan for forbidden folder, file and type
  references back this up.

### Findings

| Finding type | Rule | Severity |
|---|---|---|
| `MissingRequiredAttribute` | A required structured attribute is absent. Description text never satisfies it. There is one finding per product for product-level attributes and one per SKU for variant-level attributes. | Medium |
| `InventoryConflict` | Catalog `stockStatus` disagrees with the Inventory quantity, in either direction. `OUT_OF_STOCK` with 0 units is fine. | High |
| `PriceConflict` | Catalog `listedPrice` differs from Pricing. `compareAtPrice` is not a price claim. | High |
| `DeliveryConflict` | The Istanbul promise differs from the shipping calculation (warehouse cutoff, transit business days, as-of time). Only SKUs with stock are checked. A missing promise is not a delivery defect. | High |
| `AmbiguousReturnRule` | A return-policy cell is `Unspecified`. Yes, No and Conditional are never flagged. | Medium |

Severity is a simple V0 product heuristic (conflicts that can mislead a purchase are High, gaps are Medium), not a
universal commerce risk score.

**MisleadingDescription is intentionally deferred.** Detecting a semantic contradiction between description text and
structured data requires later semantic or AI analysis. Leaving it out is part of the experiment design, not a missing
implementation: this deterministic baseline is what later semantic analysis must measurably improve on.

### Readiness metrics vs analyzer findings

They answer different questions:

- **Readiness metrics** (Catalog Completeness, Semantic Coverage, Data Consistency) are store metrics computed per
  evaluable unit with access to hidden truth. For example, Catalog Completeness is 669 / 675 SKU x requirement units.
- **Findings** are actionable issues the analyzer detects from published data, one per root cause. For example, there
  are 6 `MissingRequiredAttribute` findings.

### Evaluation (only with `--evaluate`)

The evaluator reduces findings, defects and decoys to the same semantic identity
`CaseKey(type, target, field)` and matches them one-to-one. The target is the SKU, product or policy cell. Matching
never uses summary text or IDs, and a second finding for an already-matched defect is a duplicate false positive.

- **Precision** = true-positive findings / findings
- **Deterministic recall** = detected defects of supported types / injected defects of supported types
- **Overall recall** = detected defects / all injected defects, including the deferred semantic ones
- **Decoy false-positive rate** = decoys flagged by at least one finding / decoys

Seed 42 runtime results:

| | Value |
|---|---|
| Findings | 20 (12 High, 8 Medium) |
| Deterministic defects detected | 20 / 20, 0 false positives, 0 false negatives |
| Deterministic recall | 100.0% |
| Overall defect recall | 83.3% (20 / 24) |
| Precision | 100.0% |
| Decoys flagged | 0 / 8 |
| Deferred | MisleadingDescription: 4 |

A 40-seed test confirms that precision and deterministic recall stay at 100% with no seed-specific logic.

## Generate the dataset

```
dotnet run --project src/CommerceReadiness.DemoStore -- --seed 42
```

The command writes `demo-store/generated/seed-42/`:

```
clean/      store.json, catalog.csv, price-list.json, inventory.json, shipping.json, payments.json, returns.json
published/  same files, with defects injected
eval/       ground-truth.json, intents.json, metrics.json
analysis/   findings.json, summary.json, evaluation.json (written by the analyzer)
```

`store.json` is the store manifest: `schemaVersion` (currently 1), `storeVersion`, `kind` and `asOf`, the simulation
clock the store's delivery promises refer to. Readers reject unsupported schema versions.

`--out <dir>` changes the output directory. The seed-42 dataset is committed. Output is byte-identical for a given
seed regardless of machine culture. Intent outcomes are designed for the default simulation clock.

## Run the analyzer

```
dotnet run --project src/CommerceReadiness.Analyzer -- --dataset demo-store/generated/seed-42
dotnet run --project src/CommerceReadiness.Analyzer -- --dataset demo-store/generated/seed-42 --evaluate
```

- **Normal mode** reads `published/` only, prints a report, and writes `analysis/findings.json` and
  `analysis/summary.json`. It deletes any `evaluation.json` left by an earlier run, since that file would score a
  different set of findings.
- **`--evaluate`** additionally reads `eval/ground-truth.json` and writes `analysis/evaluation.json`. It is the only
  path that touches ground truth.

`--out <dir>` changes the analysis output directory. Output is byte-identical for the same dataset.

## Run the tests

```
dotnet build
dotnet test
```

The tests cover:

- reproducibility: same seed gives identical bytes, different seeds give different data, and 40 seeds keep the same
  shape;
- a Turkish current culture does not change the output;
- the clean store is defect-free and scores 100% on every metric;
- clean and published stores share exact denominators, and a published defect can never remove a unit;
- published non-passing units equal the union of the defects' expected impacts, every defect has an authoritative
  counterpart, and decoys stay consistent;
- inventory and pricing authority;
- the oracle uses clean data only, and its results are re-checked by an independent implementation;
- no-valid intents are verified by brute force over all SKUs;
- delivery is computed from the fixed clock;
- inventory is tracked per sneaker SKU;
- JSON and CSV round-trip;
- a regression test proving a misleading description cannot raise Semantic Coverage;
- variant evidence: a SKU-scoped fact covers only its own SKU, unscoped variant evidence never counts, and a wrong
  SKU-scoped claim fails only that SKU's claim unit;
- store manifests: the reader rejects unsupported schema versions and missing manifests;
- analyzer trust boundary: the analyzer rejects the clean store and its API takes no ground truth; the published-only
  and poisoned-folder datasets behave as described above; evaluation happens only with `--evaluate`;
- analyzer rules: every supported seed-42 defect is detected, MisleadingDescription is not, no decoy is flagged, and a
  consistent store yields zero findings;
- evaluator: correct TP/FP/FN counts, deterministic vs overall recall, duplicate findings counted as false positives,
  per-decoy hits, and matching by semantic identity only;
- analyzer determinism: stable and unique finding IDs, byte-identical analysis files, and 40 seeds analyzed
  generically.

## Not built yet (by design)

No LLM or AI calls, RAG, embeddings, vector search or pgvector; no database or EF Core; no web API or UI; no Shopify
or other external integration; no checkout or authentication; no semantic analysis of description text; no repair
flow; no shopper evaluation runner. The only dependencies are the .NET 10 SDK and xUnit.

## Roadmap

- **Milestone 1 (done)**: Deterministic Readiness Analyzer that reads only published data and is scored against this
  ground truth (precision and recall, with decoys).
- **Milestone 2**: Hybrid product retrieval: structured filters plus semantic search (pgvector).
- **Milestone 3**: LLM shopper with tool calling, producing the `ShopperResult` contract.
- **Milestone 4**: Evaluation runner and before/after comparison (published vs repaired store).
- **Milestone 5**: Thin web UI.

## Layout

```
src/CommerceReadiness.Core        domain, source-of-truth map, delivery, oracle, metrics, contracts,
                                  store storage (Storage/), readiness analyzer (Analysis/), evaluators (Evaluation/)
src/CommerceReadiness.DemoStore   CLI: seeded generator, defect/decoy injection, intents, dataset output
src/CommerceReadiness.Analyzer    CLI: published-only analysis, optional --evaluate
tests/CommerceReadiness.Tests     xUnit tests
demo-store/generated/seed-42      committed reference dataset
```
