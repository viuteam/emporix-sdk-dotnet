# Viu.Emporix.MixinSync

Generates typed C# for an Emporix tenant's **mixins** and detects schema drift.

A companion to [`Viu.Emporix`](https://www.nuget.org/packages/Viu.Emporix), the
unofficial .NET SDK for the Emporix Commerce Engine. Built and maintained by
[viu](https://www.viu.ch). Not an official product of Emporix AG.

## What it is for

A mixin is a set of tenant-defined fields stored under `entity.mixins.<key>`,
described by a JSON Schema that Emporix hosts and versions. Those fields are
specific to your tenant, so their types cannot ship inside an SDK — this tool
generates them into your own repository.

```bash
dotnet tool install --global Viu.Emporix.MixinSync
```

## Commands

```bash
emporix-mixins pull        # read the Schema Service, write the snapshot and lockfile
emporix-mixins generate    # turn the snapshot into C# types, contexts and a registry
emporix-mixins check       # compare the tenant against the lockfile; exits 1 on drift
```

`generate` reads the committed snapshot, never the network, so your build stays
offline and deterministic. Commit everything it writes.

## Configuration

`emporix-mixins.json`, beside your solution:

```json
{
  "tenant": "acme",
  "namespace": "Acme.Mixins",
  "out": "src/Acme.Shop/Mixins/Generated",
  "lockFile": "src/Acme.Shop/Mixins/mixins.lock.json"
}
```

Credentials come from `EMPORIX_BACKEND_CLIENT_ID` and `EMPORIX_BACKEND_SECRET` —
the Schema Service is seller-side — so the file holds nothing secret and belongs
in version control.

Pass a different path as the second argument: `emporix-mixins pull path/to/config.json`.

## What you get

One namespace and one `JsonSerializerContext` per mixin, plus a registry binding
them to the SDK:

```csharp
var delivery = MixinReader.Read(product.Mixins, Mixins.DeliveryOptions);

var w = MixinWriter.Create().Set(Mixins.DeliveryOptions, value);
product.Mixins          = w.Values;
product.Metadata.Mixins = w.SchemaUrls;   // Emporix leaves a mixin unvalidated without this

var paper = Condition.EqualTo("Paper");

string q = MixinQuery.For(Mixins.DeliveryOptions)
    .Where(d => d.Packaging, paper)
    .Build()
    .Build();
```

The generated code is Native AOT compatible and reflection-free, matching the
SDK's own guarantees.

## Drift detection

Emporix assigns a new schema version on every change, and nothing tells you when
it happens. That is what `check` is for — put it in your own repository:

```yaml
on:
  schedule: [{ cron: "0 6 * * *" }]
  workflow_dispatch: {}
jobs:
  drift:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
      - run: dotnet tool install --global Viu.Emporix.MixinSync
      - run: emporix-mixins pull && emporix-mixins generate
        env:
          EMPORIX_BACKEND_CLIENT_ID: ${{ secrets.EMPORIX_BACKEND_CLIENT_ID }}
          EMPORIX_BACKEND_SECRET: ${{ secrets.EMPORIX_BACKEND_SECRET }}
      - uses: peter-evans/create-pull-request@v8
        with:
          title: "chore: sync mixin schema versions"
          branch: mixins/sync
```

A raised version then arrives as a pull request with the type diff beside it.
Generating classes by hand takes ten minutes; noticing that a schema moved does
not happen by hand at all.

## Documentation

- [SDK readme](https://github.com/viuteam/emporix-sdk-dotnet#mixins) — the runtime side
- [Design spec](https://github.com/viuteam/emporix-sdk-dotnet/blob/main/docs/superpowers/specs/2026-09-03-mixin-codegen-design.md) — including the `q` forms not yet verified against a live tenant
- [Issues](https://github.com/viuteam/emporix-sdk-dotnet/issues)

## License

MIT
