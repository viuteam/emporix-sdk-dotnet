# Architecture Decision Records

One ADR per decision, at most a page each: context, options with trade-offs,
decision, consequences. An ADR is never rewritten — when a decision changes, a new
one supersedes the old and marks it as superseded.

| No. | Decision | Status |
| --- | --- | --- |
| [0001](0001-type-generation.md) | Type generation: NSwag DTO-only plus a hand-written client | Proposed |
| [0002](0002-target-framework.md) | Target framework: `net10.0` only | **Decided** |
| [0003](0003-packaging.md) | Packaging: one package | Proposed |
| [0004](0004-aot-trimming.md) | Native AOT: build compatible, do not advertise | **Decided** |
| [0005](0005-resilience.md) | Resilience: own retry logic instead of Polly | Proposed |
| [0006](0006-naming.md) | Naming: `Viu.Emporix`, MIT, public repository | **Decided** |
| [0007](0007-streaming.md) | Streaming: expose the response, parse with the framework | **Decided** |
| [0008](0008-long-running-jobs.md) | Long-running jobs: one waiting helper, no job abstraction | **Decided** |
| [0009](0009-cloud-functions.md) | Cloud functions: the caller brings the type information | **Decided** |
| [0010](0010-unknown-enum-values.md) | Unknown enum values: null where there is room, strict where there is not | **Decided** |

## What was measured

0001, 0004, 0005 and 0007 rest on spikes against the real Emporix
specifications and the real .NET 10 SDK, not on assumptions:

- **NSwag DTO-only** against `product.yml` (3,869 lines): 2,691 lines, 102
  classes, compiling with `IsAotCompatible=true` and a `JsonSerializerContext` in
  **0 warnings**.
- **Kiota** against the same specification: 132 files, 11,969 lines, two
  discriminator warnings, plus open AOT issues in its own dependencies.
- **`Microsoft.Extensions.Http.Resilience`**: **30 transitive packages**.

## Basis

[../analysis.md](../analysis.md) — analysis of the Node SDK including the feature
parity matrix.
