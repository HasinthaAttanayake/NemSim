# Driving NemSim with an LLM

Designing a sweep is mechanical work with a fiddly file format: read the baseline scenario, decide
what to vary, write twenty-five JSON merge patches without typos, run them, read the scalars back.
An LLM is good at exactly that shape of task, provided you give it the contract rather than
expecting it to infer one.

NemSim publishes that contract deliberately. This page is how to hand it over.

## Why this works

Three properties make NemSim unusually safe to point a language model at:

- **The schemas are machine-readable and self-describing.** `--describe-schema` emits JSON Schema
  (draft 2020-12) for both input formats, generated from the same constants the validator enforces,
  so the published schema cannot drift from what the tool accepts.
- **Validation is strict and early.** Deserialisation rejects unknown properties rather than
  ignoring them, and both `--fan-out-sweep` and `--run-sweep` validate every generated config before
  it runs. A hallucinated field name fails loudly in seconds, not silently after an hour of
  dispatch.
- **The model is deterministic.** There is no run-to-run noise for a bad generation to hide in. If
  a point's numbers look wrong, they are wrong for a reason you can find.

What the model cannot do is decide what is worth asking. That is your job, and
[Designing a study](index.md) covers it.

## What to give it

Four things, in this order. Everything here is a file or a command output; none of it requires
explaining NemSim in prose.

### 1. The schemas

```bash
dotnet run --project NEM.CLI -- --describe-schema scenario
```

```bash
dotnet run --project NEM.CLI -- --describe-schema sweep
```

These are the authoritative field contracts. Paste both into context. Do not paraphrase them:
paraphrase is where field names get invented.

### 2. The baseline scenario

The scenario the sweep will patch, for example `scenarios/nem-fy2026-all-regions.json`. The model
needs to see the actual structure it is patching, especially the region IDs and the technology names
used as merge keys.

It is long. If context is tight, give it the full file for one region and the region-ID list for the
rest.

### 3. The merge-patch rules

This is the part a model will get wrong if you do not state it, because it is not plain RFC 7386.
Give it verbatim:

> Point `overrides` are applied to the baseline scenario as a JSON merge patch. Four rules govern
> it, and the last two are extensions to RFC 7386:
>
> 1. `null` as a value deletes that property, as RFC 7386 specifies.
> 2. Any array not named below is replaced wholesale, as RFC 7386 specifies.
> 3. Four arrays are merged **by key** rather than replaced wholesale: `regions` keyed by
>    `regionId`; `regions[].generatingFleets` and `regions[].storageFleets` keyed by `technology`;
>    `monthlyCapacityFactors` keyed by `month`. So a point can change one fleet in one region
>    without restating the whole scenario.
> 4. `{"<key>": "...", "$remove": true}` deletes a keyed array element. The object must contain
>    exactly those two properties.

One thing that is not a merge-patch rule but that a model needs told anyway: `axisValue` is a
display value for the x-axis only. Nothing in the model reads it. The actual parameter change lives
entirely in `overrides`, and the two must agree or the resulting chart will be wrong.

### 4. The output vocabulary

The scalars a sweep publishes per point per region are fixed and named. Give the model the table
from [Outputs and provenance](../guide/outputs.md) so it asks for figures that exist, using the
names they are actually emitted under.

## A prompt scaffold

Adapt this. The structure matters more than the wording: role, contract, task, constraints, output
format.

```text
You are helping design a sweep for NemSim, a deterministic hourly grid dispatch model
for Australia's National Electricity Market.

CONTRACT
Sweep definition JSON Schema:
<paste output of: --describe-schema sweep>

Scenario config JSON Schema:
<paste output of: --describe-schema scenario>

Baseline scenario I am patching:
<paste scenarios/nem-fy2026-all-regions.json>

MERGE-PATCH RULES
<paste the four numbered rules above, verbatim, plus the axisValue note>

TASK
Produce a sweep definition that varies <ONE THING> from <LOW> to <HIGH> across
<N> points, holding everything else constant.

CONSTRAINTS
- Output one JSON document and nothing else. No commentary, no markdown fence.
- sweepId and every pointId must match ^[a-z0-9][a-z0-9-]*$.
- Include a baseline point at the unchanged value, with empty overrides.
- Every point's axisValue must equal the total change its overrides actually make.
- Space the points to resolve where behaviour changes, not uniformly, if I have
  told you where that is.
- Use only fields present in the schemas. Do not invent field names. Unknown
  properties are a hard error.
```

Then, separately, for interpreting results:

```text
Here is the sweep index from a completed run:
<paste sweeps/{sweepId}/index.json>

The scalar names, labels and units are:
<paste the scalar table>

Identify where each scalar's response to the axis changes shape, and say which
component of system cost is responsible for each change. Report the storage
sizing outcome for each point and flag any point that is not "notRequired" or
"resized".

Do not infer causation beyond what the scalars support. Do not extrapolate past
the last point. If a change could be an artefact of the sizing floor
(30 MW / 120 MWh) rather than a real requirement, say so.
```

## The loop

While you are iterating on a sweep definition, fan out on its own first:

```bash
dotnet run --project NEM.CLI -- --fan-out-sweep sweeps/my-sweep.json
```

Fan-out writes every point's materialised config **and validates each one, stopping at the first
failure**. A hallucinated field, a bad region ID or a wrong schema version all fail here, in
seconds, before any dispatch happens. Feed the error back to the model and regenerate.

Once fan-out is clean, or once you are past the iterate-and-regenerate stage entirely:

```bash
dotnet run --project NEM.CLI -- --run-sweep sweeps/my-sweep.json
```

`--run-sweep` fans out again and validates each point the same way, but records an invalid point as
that point's failure and continues on rather than stopping the whole run. That is right for a long
unattended run of many points: one bad override should not lose the results of the rest.

## Guardrails

An LLM will produce a fluent, confident reading of a sweep whether or not the sweep supports it.
These are the failure modes worth watching for.

**Check `axisValue` against `overrides` yourself.** The model will produce a definition where the
label says +3,000 MW and the overrides add 2,900. Nothing in NemSim catches it, because nothing in
NemSim reads `axisValue`. Every chart downstream will be wrong. This is the single highest-value
manual check.

**Do not let it interpret levels.** NemSim's biases are systematic and known; see
[Limitations](../assumptions/limitations.md). A comparison between two points is well supported; an
absolute figure is not. Ask for differences and shapes, not for headline numbers.

**Do not let it extrapolate.** The interesting behaviour is at the transitions, and transitions are
exactly where a fitted trend fails. If you want to know what happens at twice the axis maximum, run
that point.

**Watch for the sizing floor.** A point reporting 30 MW / 120 MWh of storage may need almost none.
That is the floor every sized candidate is raised to, not a fitted result, and a model reading the
scalars alone will not know it.

**Distinguish "not modelled" from zero.** Transmission cost is zero for an unlinked system because
there are no links; it is *absent* from a regional result because transmission is not in regional
cost scope. These are different claims and the artifacts distinguish them. A summariser will happily
flatten both to "0".

**Make it cite the point.** Require every claim to name the `pointId` it came from. Unattributable
claims are the ones that turn out to be invented.

## What it should not be asked to do

Do not ask a model to pick the input *values*. Round-trip efficiencies, capital costs, discount
rates and technical lives are the assumptions your conclusions rest on, and a plausible-sounding
number with no provenance is worse than an obviously arbitrary one. Bring sourced figures, or sweep
the range and report that you swept it. See
[Scenario parameters](../assumptions/scenario-parameters.md).

Do not ask it to summarise a result for an external audience without
[Limitations](../assumptions/limitations.md) in context. Every one of those limitations is a way a
confident summary goes wrong.

## Next

- [Sensitivity analysis](sensitivity-analysis.md): a worked example.
- [Sweeps](../guide/sweeps.md): the definition format in full.
- [Outputs and provenance](../guide/outputs.md): what a run publishes and how to read it.
