# Commerce Readiness Lab

Commerce Readiness Lab tests whether an ecommerce store can be reliably understood and used by AI shopping agents:
can an agent find the right product, respect the shopper's constraints, and avoid confidently wrong answers when the
store's own data is stale, incomplete or contradictory?

This repository is at **Milestone 0**: a deterministic synthetic store (DemoStore) plus a trustworthy evaluation
foundation. There is no AI in it yet, on purpose.

## What Milestone 0 contains

- A seeded generator that builds a **clean**, internally consistent store of 100 SKUs (78 products) in four
  categories: Backpacks, Sneakers (8 products, 30 size/color variants), Headphones and SmallElectronics.
- A **published** copy of that store with 24 deterministically injected defects and 8 decoys.
- **Ground truth** for every defect and decoy, plus the semantic facts each product description asserts.
- 20 Turkish **shopper intents** with hidden structured constraints and expected results computed by a
  deterministic **oracle** (14 with valid products, 4 with no valid product, 2 with insufficient information).
- **Readiness metrics** with explicit numerators and denominators, computed for the clean and published stores.
- The **output contract** a future LLM shopper must follow, so its answers can be evaluated deterministically.
- 76 xUnit tests proving reproducibility and internal consistency.

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

Each metric is `passed units / applicable units`. Every unit has a stable ID, and each defect or decoy maps to exactly
one unit, so tests can prove that the failures in the published store are exactly the injected defects.

1. **Catalog Completeness** = present required **structured** attributes / applicable required structured attributes.
   One unit per SKU x category requirement. Description text never counts: a backpack described as "su itici" with
   no structured `waterResistance` is still missing it.
2. **Semantic Coverage** = required attributes present structurally **or** supported by explicit text evidence /
   applicable required attributes. Text evidence counts only when it asserts the authoritative clean-store value, so a
   misleading description can never raise coverage. In Milestone 0, text evidence comes from the templated semantic
   facts in ground truth, not from extraction.
3. **Data Consistency** = passed consistency checks / applicable consistency checks. The checks are:
   - catalog stock vs inventory (one per SKU);
   - catalog price vs pricing (one per SKU);
   - Istanbul delivery promise vs calculation (one per SKU that makes a promise and has stock);
   - description claim vs authoritative attribute (one per claim);
   - sneaker variant relationship (one per variant);
   - explicit, authoritative return-policy cell (one per cell).

   This deliberately counts checks rather than "good SKUs / total SKUs". Structured attribute values are not compared
   because Milestone 0 never injects wrong structured values, only missing ones.

Required attributes per category live in `CategoryRequirements` and drive all three metrics.

Seed 42 results:

| Store | Catalog Completeness | Semantic Coverage | Data Consistency |
|---|---|---|---|
| Clean | 675 / 675 (100%) | 675 / 675 (100%) | 510 / 510 (100%) |
| Published | 669 / 675 (99.11%) | 672 / 675 (99.56%) | 490 / 508 (96.46%) |

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

## Generate the dataset

```
dotnet run --project src/CommerceReadiness.DemoStore -- --seed 42
```

The command writes `demo-store/generated/seed-42/`:

```
clean/      catalog.csv, price-list.json, inventory.json, shipping.json, payments.json, returns.json
published/  same files, with defects injected
eval/       ground-truth.json, intents.json, metrics.json
```

`--out <dir>` changes the output directory. The seed-42 dataset is committed. Output is byte-identical for a given
seed regardless of machine culture. Intent outcomes are designed for the default simulation clock.

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
- published failures equal the injected defects exactly, every defect has an authoritative counterpart, and decoys
  stay consistent;
- inventory and pricing authority;
- the oracle uses clean data only, and its results are re-checked by an independent implementation;
- no-valid intents are verified by brute force over all SKUs;
- delivery is computed from the fixed clock;
- inventory is tracked per sneaker SKU;
- JSON and CSV round-trip;
- a regression test proving a misleading description cannot raise Semantic Coverage.

## Not built yet (by design)

No LLM or AI calls, RAG, embeddings, vector search or pgvector; no database or EF Core; no web API or UI; no Shopify
or other external integration; no checkout or authentication; no analyzer or repair flow; no evaluation runner. The
only dependencies are the .NET 10 SDK and xUnit.

## Roadmap

- **Milestone 1**: Deterministic Readiness Analyzer that reads only published data and is scored against this ground
  truth (precision and recall, with decoys).
- **Milestone 2**: Hybrid product retrieval: structured filters plus semantic search (pgvector).
- **Milestone 3**: LLM shopper with tool calling, producing the `ShopperResult` contract.
- **Milestone 4**: Evaluation runner and before/after comparison (published vs repaired store).
- **Milestone 5**: Thin web UI.

## Layout

```
src/CommerceReadiness.Core        domain, source-of-truth map, delivery, oracle, metrics, contracts
src/CommerceReadiness.DemoStore   CLI: seeded generator, defect/decoy injection, intents, file output
tests/CommerceReadiness.Tests     xUnit tests
demo-store/generated/seed-42      committed reference dataset
```
