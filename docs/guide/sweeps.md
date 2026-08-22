# Sweeps

A sweep varies one input across a series of runs while holding everything else constant, so that
the shape of the system's response to that input can be read off a chart rather than reconstructed
run-by-run from a pile of scenario files. Each run in a sweep is called a **point**. Every point
starts from the same baseline scenario config and applies a small patch on top of it.

The full published JSON Schema for the sweep definition format is available from the CLI:

```bash
dotnet run --project NEM.CLI -- --describe-schema sweep
```

## The definition format

A sweep is one JSON file.

| Field | Type | Required | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | yes | Must equal the version the CLI accepts (see `--describe-schema sweep`). |
| `sweepId` | string | yes | Must match `^[a-z0-9][a-z0-9-]*$`. Becomes the name of the `sweeps/<sweepId>/` output directory, hence the restricted character set. |
| `name` | string | yes | Human-readable name. |
| `axis` | object | yes | `label` and `unit`, both non-blank strings, giving how the swept quantity is captioned on a chart. |
| `baselineConfigPath` | string | yes | Path to the scenario config every point patches. |
| `points` | array of [point](#points) | yes, at least one | The runs that make up the sweep. |

### `points[]`

| Field | Type | Required | Meaning |
|---|---|---|---|
| `pointId` | string | yes | Must match `^[a-z0-9][a-z0-9-]*$` and be unique within the sweep. Becomes part of output filenames. |
| `axisValue` | number | yes | Where this point sits on the chart's x-axis. Display only; see below. |
| `label` | string | yes | Human-readable label for this point. |
| `overrides` | object | yes | A JSON merge patch applied to the baseline config. May be empty (`{}`) for a true baseline point. |

## `axisValue` does not drive anything

This is the single most important thing to understand about the format. `axisValue` is a plotting
coordinate and nothing else: it places the point on the x-axis of a chart, and has no effect on
what the point actually runs. The only thing that changes what a point runs is `overrides`.

A sweep is free to declare `axisValue: 500` on a point whose `overrides` changes nothing, or changes
something entirely unrelated to "500" in any unit. Nothing in `--fan-out-sweep` or `--run-sweep`
cross-checks the two. Such a sweep will fan out, run, and publish a perfectly normal-looking chart
whose x-axis has no relationship to what was actually varied. When you write a sweep, treat keeping
`axisValue` and `overrides` in agreement as entirely your responsibility. No validation will catch
a mismatch for you.

## JSON merge-patch semantics

Each point's `overrides` is applied to the baseline scenario config as an RFC 7386 JSON merge patch,
with two deliberate extensions, both about arrays: four named arrays merge by key rather than being
replaced, and a reserved `$remove` shape deletes a keyed element. This is currently documented
nowhere else, so it is worth being precise about.

**Scalars and objects.** A property present in the patch replaces the corresponding property in the
target. A property whose value is `null` in the patch deletes that property from the target
entirely, rather than setting it to null. Objects are merged key by key, recursively.

```json
// baseline
{ "costBasis": { "year": 2026, "realDiscountRate": 0.07 } }

// patch
{ "costBasis": { "realDiscountRate": 0.05 } }

// result
{ "costBasis": { "year": 2026, "realDiscountRate": 0.05 } }
```

```json
// patch that deletes a property
{ "storageSizing": { "reliabilityStandardName": null } }
```

**Most arrays are replaced wholesale**, exactly as plain RFC 7386 specifies: if a property's patch
value is an array and it is not one of the keyed arrays below, the whole array in the patch replaces
the whole array in the target.

**Four arrays are the exception** and are merged by key rather than replaced outright, so a point
can change one element without restating the rest of the array:

| Array | Key field |
|---|---|
| `regions` | `regionId` |
| `regions[].generatingFleets` | `technology` |
| `regions[].storageFleets` | `technology` |
| `monthlyCapacityFactors` | `month` |

For each of these, a patch item whose key matches an existing target item is merged into that item
(recursively, by the same rules); a patch item whose key does not match any existing item is
appended as a new item.

```json
// baseline region
{ "regionId": "NSW1", "dataCentreNameplateMw": 0, "generatingFleets": [ /* ... */ ] }

// patch: change one field on the existing NSW1 region, leave its fleets untouched
{ "regions": [ { "regionId": "NSW1", "dataCentreNameplateMw": 500 } ] }
```

This is exactly the shape used by the worked example below, and by `sweeps/datacentre-nameplate-fy2026.json`
in this repository: each point overrides `dataCentreNameplateMw` on a handful of regions without
needing to repeat every region's generating and storage fleets.

**Deleting a keyed array element** uses a reserved shape: an object containing *only* the key field
and `"$remove": true`.

```json
// remove the Wind fleet from NSW1 without touching any other fleet
{
  "regions": [
    {
      "regionId": "NSW1",
      "generatingFleets": [
        { "technology": "Wind", "$remove": true }
      ]
    }
  ]
}
```

An object with `$remove: true` plus any other property, other than the key field, is rejected. The
merge cannot tell whether you meant to remove the item or edit it.

## Workflow: fan out, then run

```bash
dotnet run --project NEM.CLI -- --fan-out-sweep sweeps/my-sweep.json
```

`--fan-out-sweep` applies every point's patch to the baseline, writes each resulting scenario config
to `sweeps/<sweepId>/configs/<pointId>.json`, and validates every config it writes (via the same
loading and validation `--run-scenario` uses), stopping at the first invalid point. It does not run
any dispatch. This is the fast feedback loop: while you are developing or debugging a sweep, run
fan-out repeatedly and read the generated configs to confirm each point patches what you intended.

```bash
dotnet run --project NEM.CLI -- --run-sweep sweeps/my-sweep.json
```

`--run-sweep` fans out again internally, writing the same `sweeps/<sweepId>/configs/*.json` files,
and validates each config the same way `--fan-out-sweep` does. The difference is what happens when
a point is invalid: `--run-sweep` records it as that point's failure and continues on to the
remaining points, so one malformed override cannot abandon a long unattended run, whereas
`--fan-out-sweep` stops immediately so you get fast feedback while iterating.

You can run `--run-sweep` directly; there is no need to fan out first to catch a malformed override,
since `--run-sweep` validates every point itself. Running `--fan-out-sweep` first is still useful
while you are actively developing a sweep, so you can read the generated configs before committing
to a full dispatch run.

## What a run produces, and what happens when a point fails

Each point's dispatch result, status, and configuration are published under
`sweeps/<sweepId>/`. See [Outputs and provenance](outputs.md) for the full artifact layout. The
detail relevant here is failure handling: a sweep does not stop at the first failing point. Each
point gets its own `<pointId>.status.json` recording whether it succeeded or failed; if a point
fails, whatever partial dispatch artifacts it produced are deleted so a failed point never leaves
misleading output behind, and the run continues on to the remaining points. Only once every point
has been attempted does `--run-sweep` exit; if any point failed, the process exit code is `1` and
the failed point IDs are reported.

The sweep as a whole is not published atomically. Point results, status files, the sweep index and
the manifest are written as the run proceeds, so an interrupted sweep can leave a partially updated
`sweeps/<sweepId>/` directory. Rerun the sweep to bring it back into a consistent state.

## A complete minimal worked example

A two-point sweep varying storage capital cost, run against the committed FY2026 example scenario.

```json
{
  "schemaVersion": 1,
  "sweepId": "battery-capex-sensitivity",
  "name": "Battery capital cost sensitivity",
  "axis": {
    "label": "Battery power capital cost",
    "unit": "AUD/MW"
  },
  "baselineConfigPath": "scenarios/nem-fy2026-all-regions.json",
  "points": [
    {
      "pointId": "baseline",
      "axisValue": 470000,
      "label": "Baseline (470,000 AUD/MW)",
      "overrides": {}
    },
    {
      "pointId": "half-capex",
      "axisValue": 235000,
      "label": "Half capital cost",
      "overrides": {
        "regions": [
          {
            "regionId": "NSW1",
            "storageFleets": [
              {
                "technology": "Battery",
                "costParameters": {
                  "powerCapitalCostAudPerMw": 235000
                }
              }
            ]
          }
        ]
      }
    }
  ]
}
```

The first point's `overrides` is empty, so it reproduces the baseline exactly; its `axisValue`
records what the baseline's actual Battery capital cost is, for the chart's sake. The second point
changes only `powerCapitalCostAudPerMw` on NSW1's `Battery` fleet. The keyed-array merge means the
rest of that fleet's cost parameters, its technology profile, and every other region's fleets are
carried over from the baseline untouched.

## See also

- [Scenario configuration](scenarios.md): the format every point's `overrides` patches.
- [Outputs and provenance](outputs.md): the full artifact layout a sweep run produces.
