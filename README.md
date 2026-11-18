# Commerce Readiness Lab

Commerce Readiness Lab tests whether an ecommerce store can be reliably understood and used by AI shopping agents:
can an agent find the right product, respect the shopper's constraints, and avoid confidently wrong answers when the
store's own data is stale, incomplete or contradictory?

This repository is at **Milestone 3**. It contains:

- a deterministic synthetic store (DemoStore) with a trustworthy evaluation foundation (Milestone 0);
- a deterministic Readiness Analyzer that reads only published data, scored by a separate evaluator (Milestone 1);
- hybrid product retrieval: embeddings, a pgvector index, and structured, semantic and hybrid search, with its own
  retrieval benchmark (Milestone 2);
- a grounded LLM shopper with read-only commerce tools, structured `ShopperResult` output and a deterministic
  `ShopperEvaluator` (Milestone 3).

## What Milestone 0 contains

- A seeded generator that builds a **clean**, internally consistent store of 100 SKUs (78 products) in four
  categories: Backpacks, Sneakers (8 products, 30 size/color variants), Headphones and SmallElectronics.
- A **published** copy of that store with 24 deterministically injected defects and 8 decoys.
- **Ground truth** for every defect and decoy, plus the semantic facts each product description asserts.
- 20 Turkish **shopper intents** with hidden structured constraints and expected results computed by a
  deterministic **oracle** (14 with valid products, 4 with no valid product, 2 with insufficient information).
- **Readiness metrics** with explicit numerators and denominators, computed for the clean and published stores.
- The **output contract** shape that Milestone 3's LLM shopper must follow, so its answers can be evaluated
  deterministically.

## What Milestone 1 adds

- A **Readiness Analyzer** with five explicit rules. It reads only the published store and emits actionable
  **findings**, each with evidence and a suggested fix.
- A separate **AnalyzerEvaluator** that scores findings against ground truth: precision, deterministic recall,
  overall recall and decoy hits.
- An analyzer CLI whose normal mode never touches the clean store or ground truth.
- A `store.json` manifest per store folder (schema version, store version, kind, as-of time).

## What Milestone 2 adds

- One deterministic **semantic search document per published SKU**, with a content hash. Volatile commerce facts are
  never embedded.
- **Embeddings** through `Microsoft.Extensions.AI` `IEmbeddingGenerator`. The runtime uses the OpenAI adapter
  (`text-embedding-3-small`, 1536 dimensions); tests and CI use a deterministic offline hashing generator.
- **PostgreSQL + pgvector** (Docker) with exact cosine search, an idempotent content-hash-aware indexer, and an
  in-memory reference index.
- Three search modes, **StructuredOnly**, **SemanticOnly** and **Hybrid**, through a small retrieval CLI.
- A generated **retrieval benchmark** (`eval/retrieval-benchmark.json`) and Recall@K, Precision@K and HitRate@K
  per mode.
- 210 xUnit tests in total: 205 unit tests and 5 pgvector integration tests.
- Reusable runtime extracted to `CommerceReadiness.Retrieval.Runtime` (config-source agnostic).

## What Milestone 3 adds

- An OpenAI-powered **grounded shopper** (`CommerceReadiness.Shopper`) over Microsoft.Extensions.AI:
  Responses API + function invocation + structured `ShopperResult`.
- Six **read-only** tools. `search_products` always uses Milestone 2 Hybrid retrieval; product attributes, inventory,
  delivery, returns and payments are verified separately. SKU-anchored tools resolve category/price/rules server-side.
- Application-issued **evidence IDs** (`EVID-001`, …), a tool trace, and a deterministic Turkish renderer (no second
  LLM call for prose).
- A deterministic **ShopperEvaluator**: outcome accuracy, mandatory-constraint grounding, confident-wrong and
  unsupported-claim rates, grounded NoMatch, typed clarification, all-attempted denominators. No LLM-as-judge.
- Offline scripted-chat tests; real OpenAI only via `ask` / `benchmark`.

See [docs/shopper-contract.md](docs/shopper-contract.md).

### Observed OpenAI benchmark (seed 42, 20 intents × 3 runs)

Provenance: `gpt-6.2-sol` / `shopper-prompt-v1` / embeddings `text-embedding-3-small` 1536 / Hybrid K=5 /
`pgvector` / file `shopper-runs/benchmark-gpt-6.2-sol-shopper-prompt-v1.json`.

| Metric | Value |
|---|---|
| RunCompletionRate | 1.000 (60/60) |
| OutcomeAccuracy | 0.950 (57/60) |
| PrimaryRecommendationSuccessRate | 0.929 (39/42) |
| GroundedRecommendationSuccessRate | 0.714 (30/42) |
| ConfidentWrongRate | 0.000 (0/95) |
| UnsupportedClaimRate | 0.000 (0/433) |
| CorrectNoMatchRate | 1.000 (12/12) |
| GroundedNoMatchRate | 1.000 (12/12) |
| CorrectClarificationRate | 1.000 (6/6) |
| OutcomeStabilityRate | 1.000 (20/20) |

I07 (conditional opened-return headphones) was a stable false abstention (`NoMatch` × 3). Ground truth was not
changed. I17/I18 stayed `NoMatch`; I19/I20 stayed typed `NeedsClarification`.

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

- The Milestone 3 shopper sees only `text`; the oracle sees only `constraints`.
- `OracleShopper` reads only the clean store and throws if given anything else. It returns an explicit
  `IntentOutcome` (`ValidProductsExist`, `NoValidProduct` or `InsufficientInformation`), the valid SKUs, the forbidden
  SKUs in the category with the constraints each violates, and any missing information.
- Valid-product intents are anchored on a seeded in-stock SKU, so they stay valid for any seed. No-valid intents ask
  for combinations the generator never produces, such as size 47 or next-day delivery to Ankara. Generation fails if
  the oracle's outcome differs from the intended one.
- `forbidden[].looksValidInPublished` marks traps: SKUs that broken published data makes look valid. Intent I02 always
  contains one, a SKU the catalog still shows as `IN_STOCK` although inventory holds 0 units.
- For `NoValidProduct` and `InsufficientInformation`, the correct shopper behavior is `NoMatch` or
  `NeedsClarification`, not bending a constraint.

## Shopper contract and evaluation (Milestone 3)

Defined in `CommerceReadiness.Core.Shopping` and scored by `CommerceReadiness.Core.Evaluation.Shopping.ShopperEvaluator`.

```
ShopperResult  { SchemaVersion, Outcome, Clarification?, Recommendations[], NoMatchReason?, SupportingEvidenceCallIds[] }
Recommendation { Sku, Confidence (Confirmed|Uncertain), Claims[] }
GroundedClaim  { Type, AttributeField?/City?/ReturnQuestion?, Value, EvidenceCallIds[] }
```

- **Unsupported claim**: missing, unknown, wrong-source, failed, wrong-entity or contradicted evidence.
- **Confident wrong recommendation**: a Confirmed SKU that violates at least one mandatory constraint under clean truth.
- **Grounded recommendation**: clean-valid primary SKU, every mandatory constraint supported by the right tool evidence,
  no contradicted claim.
- **NoMatch / NeedsClarification**: mapped from oracle `NoValidProduct` / `InsufficientInformation`. Clarification
  must include the expected typed missing fields.

See [docs/shopper-contract.md](docs/shopper-contract.md) and [docs/evaluation-contract.md](docs/evaluation-contract.md).

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

## Hybrid Product Retrieval (Milestone 2)

Milestone 2 answers a narrower question than the final shopper: **given a product-discovery intent, can we retrieve
the right candidate SKUs?** It covers embedding generation, vector indexing, semantic retrieval, hybrid retrieval and
retrieval evaluation. It is the retrieval foundation for RAG, not yet a RAG application: nothing generates grounded
output from the retrieved context until the Milestone 3 shopper does. No generative model, `IChatClient`, tool calling or
intent parsing is used.

```
Shopper query --+--> structured filters (exact) ------+
                |                                     +--> candidate SKUs --> ranked results
                +--> query embedding --> pgvector ----+
```

### Structured vs semantic responsibilities

The principle: exact numeric or categorical meaning goes to deterministic filters; fuzzy buyer meaning goes to semantic
retrieval.

| Responsibility | Constraints |
|---|---|
| Hard filters (`StructuredFilters`) | category, max price (Pricing), in stock (Inventory), shoe size, color, minimum laptop size, capacity, battery hours and warranty months |
| Semantic (query text only) | weather or water suitability, product type and use intent, target group, noise cancellation, microphone |
| Excluded (later shopper tools) | delivery city and deadline, installments, return requirements |

"4.000 TL altında", "43 numara" and "stokta" are filters. "Yağmurda kullanabileceğim", "işe giderken" and
"toplantılarda kullanabileceğim" are semantic. Retrieval is candidate generation: it never decides delivery, return or
installment eligibility, or whether a recommendation is safe.

### Retrieval unit and semantic document

There is one search document per published SKU. Results are SKUs, sneaker size and color live on the SKU, price and
stock are per SKU, and with roughly 100 SKUs simplicity beats de-duplicating product text.

The semantic text is deterministic Turkish:

```
Ürün: <title>
Kategori: <category>
Marka: <brand>
Ürün tipi: <type>
Açıklama: <published description>
Özellikler: Malzeme: ...; Su dayanımı: su itici; Laptop bölmesi: 15,6 inç; ...
Varyant: 43 numara, Siyah
```

It hashes to a SHA-256 content hash over a versioned template.

- **Not embedded:** price, stock, delivery promises, installments and return eligibility. They change often and belong
  to structured or tool sources.
- **Filter fields:** price and quantity come from the authoritative published Pricing and Inventory files, not catalog
  projections. A SKU the catalog shows as `IN_STOCK` with 0 units is therefore filtered out.

### Embeddings, index and search modes

- **Embedding providers.** `IEmbeddingGenerator<string, Embedding<float>>` is the only abstraction the retrieval code
  uses.
  - The OpenAI adapter (`Microsoft.Extensions.AI.OpenAI`, `text-embedding-3-small`, 1536 dimensions, configurable) is
    composed only in the retrieval project.
  - `HashingEmbeddingGenerator` is a deterministic, lexical, offline stand-in for tests and CI. It does not measure
    semantic quality.
  - Core references no AI or database package.
- **Index.** PostgreSQL + pgvector with exact cosine search. The demo corpus is intentionally small, so exact search
  is preferable; approximate vector indexing (HNSW/IVFFlat) would only be justified at larger scale.
- **Indexing** is idempotent:
  - unchanged content hashes are never re-embedded;
  - a changed document is re-embedded alone;
  - price and stock changes update the row without re-embedding;
  - unpublished SKUs are deleted.
- **Search modes:**
  - **StructuredOnly**: hard filters with neutral SKU ordering ("filters without relevance ranking").
  - **SemanticOnly**: cosine similarity over the whole index, no filters.
  - **Hybrid**: `WHERE <filters> ORDER BY cosine distance LIMIT K`.

  Both the pgvector and the in-memory index compare distances at 1e-6 precision and break ties by SKU ordinal, so they
  rank identically.

### Benchmark methodology

`eval/retrieval-benchmark.json` is generated by DemoStore. Relevant SKUs are computed by an evaluation-only oracle from
the hidden clean store using the existing shopper-constraint semantics; nothing is hand-labelled.

- **Shopper-derived (20 cases):** the intent text verbatim plus the policy's hard filters. Delivery, installment and
  return constraints are dropped, so I17 and I18 have relevant products even though their final shopping outcome is
  "no valid product".
- **Discovery-only (10 cases):** templates whose differentiating need is semantic, for example "Kalabalık metroda dış
  sesi kesen bir kulaklık". Generation fails if hard filters alone would already return exactly the relevant set.
- **Scored:** only `RelevantProductsExist` cases (26). `NoRelevantProduct` cases (2) are reported separately, since
  retrieval returning candidates is not "wrong" by itself. Insufficient-information cases (2) are excluded, because the
  shopper should ask first.
- **Semantic-discriminating subset (19 cases):** cases where the filtered pool is larger than the relevant set.
- **Metrics:** macro averages.
  - Recall@K = relevant in top K / all relevant.
  - Precision@K = relevant in top K / returned.
  - HitRate@K = share of cases with at least one relevant SKU in the top K.

  Max achievable Recall@5 is shown because some cases have more than 5 relevant SKUs.

**What the benchmark measures.** Relevance comes from hidden clean truth while indexing and search run on published
observable data. The benchmark therefore measures the commerce retrieval readiness of the published data plus the
retrieval strategy, not isolated embedding-model quality. Published defects legitimately lower scores, and the
retrieval pipeline never "corrects" published data. For example, `SKU-BP-011` is truly waterproof, but its published
structured water resistance is missing, so it is relevant to R02 ("sağanak yağmurda...") yet absent from the
semantic and hybrid top 5.

**Retrieval relevance is not factual correctness.** Semantic search finds text by meaning; it does not check whether
the text is true. A misleading published description (for example "tamamen su geçirmez" on the water-repellent
`P-BP-009`) may be retrieved for a waterproof query, and the benchmark correctly counts it as non-relevant.
MisleadingDescription stays unresolved in Milestone 2; truth checking belongs to later semantic analysis and shopper
reasoning.

### Results (seed 42, K = 5)

**Reproducible offline baseline (hashing embeddings, `retrieval/benchmark-hashing-v1.json`).** This run is
byte-identical on every machine, and identical on the in-memory and pgvector indexes.

| Mode | Group | Recall@5 | Precision@5 | HitRate@5 |
|---|---|---|---|---|
| StructuredOnly | Shopper-derived (16) | 0.576 | 0.625 | 0.875 |
| | Discovery-only (10) | 0.366 | 0.480 | 0.900 |
| | Combined (26) | 0.495 | 0.569 | 0.885 |
| | Semantic-discriminating (19) | 0.357 | 0.411 | 0.842 |
| SemanticOnly | Shopper-derived | 0.583 | 0.463 | 1.000 |
| | Discovery-only | 0.216 | 0.260 | 0.700 |
| | Combined | 0.442 | 0.385 | 0.885 |
| | Semantic-discriminating | 0.367 | 0.379 | 0.842 |
| Hybrid | Shopper-derived | 0.781 | 0.838 | 1.000 |
| | Discovery-only | 0.470 | 0.560 | 0.900 |
| | Combined | 0.662 | 0.731 | 0.962 |
| | Semantic-discriminating | 0.585 | 0.632 | 0.947 |

- Max achievable Recall@5 (combined) is 0.836.
- Provenance: `Hashing` / `hashing-v1` / 256 dims / K=5 / `memory` (matches pgvector under the shared ranking rule).

Reading the baseline: a lexical hashing model is weak at paraphrases (SemanticOnly discovery-only recall is 0.216),
yet combined with exact filters it already beats filters alone.

**Observed real-provider run (OpenAI `text-embedding-3-small`, `retrieval/benchmark-text-embedding-3-small.json`).**
Provenance: `OpenAI` / `text-embedding-3-small` / 1536 dims / K=5 / `pgvector` /
`runTimestampUtc=2026-11-18T14:12:32.2941183+00:00`. This is an observed run; the hashing file remains the
reproducible offline baseline.

Semantic-discriminating subset breakdown (9 shopper-derived + 10 discovery-only = 19):

| Mode | Group | Recall@5 | Precision@5 | HitRate@5 |
|---|---|---|---|---|
| StructuredOnly | Shopper-derived discriminating (9) | 0.3475 | 0.3333 | 0.7778 |
| | Discovery-only discriminating (10) | 0.3664 | 0.4800 | 0.9000 |
| | Combined discriminating (19) | 0.3574 | 0.4105 | 0.8421 |
| SemanticOnly | Shopper-derived discriminating | 0.6553 | 0.6000 | 1.0000 |
| | Discovery-only discriminating | 0.3306 | 0.3800 | 0.9000 |
| | Combined discriminating | 0.4844 | 0.4842 | 0.9474 |
| Hybrid | Shopper-derived discriminating | 0.7460 | 0.7111 | 1.0000 |
| | Discovery-only discriminating | 0.5239 | 0.5800 | 1.0000 |
| | Combined discriminating | 0.6290 | 0.6421 | 1.0000 |

Hybrid is the accepted Milestone 2 mode on this discriminating subset. The retrieval algorithm is not tuned further
against these numbers.

**Observed per-query regressions (documented, not tuned away):**

| Case | Query need | StructuredOnly Recall@5 | Hybrid Recall@5 |
|---|---|---|---|
| R03 | 15.6" laptop bag that stays dry in rain | 0.3333 | 0.1667 |
| R05 | Budget headset usable in meetings / calls | 0.5000 | 0.3750 |

On both cases Hybrid underperforms filters alone (OpenAI run). They remain known regressions.

**NoRelevantProduct (I15, I16):** Hybrid (and StructuredOnly) correctly returned empty results for 2/2 cases.
SemanticOnly returned 5 candidates for both. Nearest-neighbor retrieval alone cannot reliably express “no suitable
product”; empty Hybrid results come from hard filters emptying the pool, not from the embedding saying “none”.

**Insufficient-information cases** (I19, I20) stay excluded from retrieval scoring. Clarification belongs to the later
shopper reasoning layer, not to candidate generation.

**Known limitation — SKU variant saturation.** Ranking is per SKU. Near-duplicate size/color variants of the same
sneaker can fill top-K (especially under SemanticOnly; e.g. I03/I04/I11/R10). Product-level diversification is
deferred; it is out of scope for Milestone 2.

See [docs/retrieval-contract.md](docs/retrieval-contract.md) for the full contract.

## Generate the dataset

```
dotnet run --project src/CommerceReadiness.DemoStore -- --seed 42
```

The command writes `demo-store/generated/seed-42/`:

```
clean/      store.json, catalog.csv, price-list.json, inventory.json, shipping.json, payments.json, returns.json
published/  same files, with defects injected
eval/       ground-truth.json, intents.json, metrics.json, retrieval-benchmark.json
analysis/   findings.json, summary.json, evaluation.json (written by the analyzer)
retrieval/  benchmark-<model>.json (written by the retrieval benchmark)
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

## Run retrieval

Start the database. Docker is used for this dependency only.

```
docker compose up -d      # PostgreSQL + pgvector on localhost:5433
docker compose down       # stop (add -v to drop the data volume)
```

Configuration comes from environment variables or user-secrets; see `.env.example` (placeholders only, never commit
secrets):

- `ConnectionStrings__RetrievalDb` defaults to the compose database:
  `Host=localhost;Port=5433;Database=commerce_readiness;Username=commerce;Password=commerce_dev`.
- `OPENAI_API_KEY`, for example `dotnet user-secrets set OPENAI_API_KEY <key> --project src/CommerceReadiness.Retrieval`.
- `Retrieval__EmbeddingModel` (default `text-embedding-3-small`) and `Retrieval__EmbeddingDimensions` (default 1536).

```
dotnet run --project src/CommerceReadiness.Retrieval -- db-init
dotnet run --project src/CommerceReadiness.Retrieval -- index --dataset demo-store/generated/seed-42
dotnet run --project src/CommerceReadiness.Retrieval -- search --query "yağmurda kullanılabilecek laptop çantası" \
  --category Backpacks --max-price 4000 --in-stock --top 5 --mode Hybrid
dotnet run --project src/CommerceReadiness.Retrieval -- benchmark --dataset demo-store/generated/seed-42 --top 5
```

- `--embeddings openai|hashing` (default `openai`) selects the provider.
- `--index pgvector|memory` (default `pgvector`) selects the index; `--embeddings hashing --index memory` runs fully
  offline.
- `index` and `search` read only `published/`. `benchmark` is the only command that also reads
  `eval/retrieval-benchmark.json`.
- Vector rows and database volumes are never committed. The index is reproducible from the committed published data.

## Run the tests

```
dotnet build
dotnet test                                   # unit tests; pgvector tests are skipped
docker compose up -d
$env:RETRIEVAL_TEST_DB = "Host=localhost;Port=5433;Database=commerce_readiness;Username=commerce;Password=commerce_dev"
dotnet test --filter Category=Integration     # pgvector integration tests
```

No test calls OpenAI or needs an API key.

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
  generically;
- retrieval documents: determinism, no volatile facts, authoritative price and stock, content hash, SKU scope;
- indexing: no re-embedding when unchanged, single re-embed on change, price refresh without re-embedding, removal,
  schema rejection;
- search: exact filters (including numeric thresholds and missing values), neutral StructuredOnly ordering, hybrid
  filters before ranking, SKU tie-break;
- retrieval trust boundary: reflection, a narrow source scan, published-only and poisoned datasets, and a clear error
  without an API key;
- retrieval benchmark: metric formulas, oracle relevance, filter policy, discovery guards, byte-identical offline
  runs, provenance;
- pgvector integration: idempotent schema, exact cosine order on controlled vectors, seed-42 parity with the reference
  index, idempotent re-indexing, and the identical hashing benchmark.

## Run the shopper

```
dotnet run --project src/CommerceReadiness.Shopper -- ask \
  --dataset demo-store/generated/seed-42 \
  --query "4.000 TL altında, yağmurda kullanabileceğim ve laptop taşıyan bir sırt çantası istiyorum" \
  --show-tools

dotnet run --project src/CommerceReadiness.Shopper -- benchmark \
  --dataset demo-store/generated/seed-42 --runs 3
```

Requires `OPENAI_API_KEY` (same user-secrets id as Retrieval: `commerce-readiness-retrieval`). Ordinary unit tests do
not call OpenAI. Use `--embeddings hashing --index memory` only for offline scripted tests.

## Not built yet (by design)

Not built yet:

- multi-turn conversation state, MCP, web API or UI;
- HNSW/ANN tuning, product diversification / reranking;
- EF Core, Shopify or other external integration, checkout, authentication;
- semantic truth-checking of description text as a repair flow;
- LLM-as-judge.

Dependencies:

- **Core:** none beyond the .NET 10 SDK.
- **Retrieval.Runtime:** `Microsoft.Extensions.AI.OpenAI`, `Npgsql`, `Pgvector`.
- **Retrieval / Shopper executables:** configuration packages; Shopper also references `Microsoft.Extensions.AI`.
- **Tests:** xUnit.

## Roadmap

- **Milestone 1 (done)**: Deterministic Readiness Analyzer that reads only published data and is scored against this
  ground truth (precision and recall, with decoys).
- **Milestone 2 (done)**: Hybrid product retrieval: structured filters plus semantic search (pgvector), with a
  retrieval benchmark.
- **Milestone 3 (done)**: Grounded LLM shopper with tool calling, structured `ShopperResult`, and deterministic
  evaluation.
- **Milestone 4**: Evaluation runner and before/after comparison (published vs repaired store).
- **Milestone 5**: Thin web UI.

## Layout

```
src/CommerceReadiness.Core                 domain, oracle, metrics, shopper contracts, analyzers, evaluators
src/CommerceReadiness.DemoStore            CLI: seeded generator, defect/decoy injection, intents, benchmarks
src/CommerceReadiness.Analyzer             CLI: published-only analysis, optional --evaluate
src/CommerceReadiness.Retrieval.Runtime    reusable Hybrid search, indexer, pgvector/in-memory indexes, embeddings
src/CommerceReadiness.Retrieval            CLI: db-init, index, search, retrieval benchmark (loads config)
src/CommerceReadiness.Shopper              CLI: grounded ask + shopper benchmark (loads config)
tests/CommerceReadiness.Tests              xUnit tests (pgvector tests tagged Category=Integration)
demo-store/generated/seed-42               committed reference dataset
docker-compose.yml                         PostgreSQL + pgvector for retrieval only
docs/                                      evaluation, retrieval and shopper contracts
```
