# Proving it against the live tenant

Unit tests here catch typos and regressions, not wrong calls: a stubbed
`HttpMessageHandler` answers with whatever the test author already believed, so
a wrong call and its test agree with each other. `SpecPathTests` closes the
address half. A body the API rejects, a response that deserialises to nothing, a
write the server accepts and discards — only a real call finds those.

## Running it

Credentials live in `~/.emporix-smoke.env`, outside the repo. Source it, never
read it; never echo a variable from it; never `cat` it.

```bash
set -a; . ~/.emporix-smoke.env; set +a
dotnet build --configuration Release
dotnet run --project samples/Viu.Emporix.SmokeTest -c Release --no-build
```

Reading the result:

| Outcome | Meaning |
|---|---|
| `FAIL` | yours |
| `SCOPE` | the address was right and the client is not entitled — a tenant's configuration, not the SDK's problem |
| `EMPTY` | the call was understood and the tenant has nothing configured for it |

Tenant `viu` refuses `importtool` and `changelog` for missing scopes. That pair
is the expected baseline, not news.

## A new endpoint belongs in the smoke test, not in a probe

This is the sharpest lesson this repo has, and it cost a release to learn:

> A one-off probe for the media patch was written to exercise the path its
> author had in mind, and it passed. The same calls added to the smoke test
> failed on the first run — because the smoke test creates its asset the way a
> caller would, without references, and Emporix discards a reference written
> onto an asset that has none, answering `204` with no body. The probe had
> seeded the asset with a reference and never met the case. The half-working
> method had already shipped.

A probe tells you whether the call *can* work. A smoke-test step tells you
whether it works for someone who did not write it. Prefer the second.

Three things make such a step earn its keep:

1. **Read back after every write.** Several Emporix writes answer `204` and
   discard the change — whole-array writes to `refIds`, `productType` in a
   product `PATCH`. A status code proves the request was accepted, nothing more.
2. **Clean up unconditionally.** Create what you need, delete it at the end, and
   let the delete run even when a step before it failed. A crashed probe left
   two assets in the tenant once.
3. **Start from the state a caller starts from.** A fixture arranged to make the
   call succeed is the probe mistake above.

The smoke test has three passes: the anonymous storefront flow, the read-only
seller pass, and the media pass that writes and undoes everything it does. A new
write belongs in the third, or in a fourth of its own when it needs different
credentials.

## What cannot be proved

Anything the tenant's credentials cannot reach stays **unverified against a live
tenant**, and the PR says so per method rather than implying coverage. The same
goes for a service the tenant has not configured: an `EMPTY` step proves the
address and the scope, not the response shape.

Writes that cannot be undone — an order, a quote whose deletability is unknown,
anything that consumes a number — do not go in the smoke test and do not get
probed without asking first. Say what a probe would leave behind before running
it.
