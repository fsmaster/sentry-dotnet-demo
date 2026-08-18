# HelloSentry — .NET source-linking demo for Sentry

A deliberately tiny .NET 10 console app that throws interesting exceptions, so you can
watch a Sentry stack frame resolve all the way back to **the source file in this GitHub
repository**, line number included.

The DSN is configurable and ships empty — the app runs fine with Sentry switched off.

```
HelloSentry@1.0.0+9f2c1ab
Sentry enabled  env=local  dsn=https://***@o0.ingest.sentry.io/1234

Captured exception  event_id=8f1c...
```

---

## 1. Run it

```bash
dotnet run --project src/HelloSentry -- hello
```

| Command | What it does |
|---|---|
| `hello` | Prints a greeting. Sends nothing. (default) |
| `message` | Sends an info-level message with a breadcrumb. |
| `crash` | Handled `DivideByZeroException`, 3 frames across 3 files. |
| `nested` | `InvalidOperationException` wrapping an `ArgumentException` — two stack traces. |
| `unhandled` | Lets the exception escape `Main`; the SDK reports it, then the process dies. |

## 2. Configure the DSN

Three ways, last one wins:

1. **Config file** — [`src/HelloSentry/appsettings.json`](src/HelloSentry/appsettings.json):
   ```json
   { "Sentry": { "Dsn": "https://<key>@<host>/<project-id>", "Environment": "local" } }
   ```
   `appsettings.Production.json` overlays it when `DOTNET_ENVIRONMENT=Production`.
2. **Environment variable** — `SENTRY_DSN` (or `Sentry__Dsn` for the nested-key form).
3. **Command line** — `dotnet run --project src/HelloSentry -- crash --dsn https://...`

Every key under `Sentry:` binds straight onto `SentryOptions`, so `Debug`,
`TracesSampleRate`, `SendDefaultPii`, `MaxBreadcrumbs` and friends are all configurable
without touching code.

---

## 3. The actual point: seeing .NET source in the Sentry UI

.NET has no source maps. The equivalent is a **portable PDB** stamped by **Source Link**
with the repository URL and the exact commit SHA that produced the binary. Upload that PDB
to Sentry, connect Sentry to GitHub, and every frame becomes a link to the right blob at
the right line.

```
   your source --build--> HelloSentry.dll + HelloSentry.pdb
                              |                    |
                              |     Source Link stamps repo URL + commit SHA
                              |                    |
   crash --> Sentry event ----+---> sentry-cli debug-files upload
                                        |
                    Sentry matches the event's debug-id to the PDB,
                    gets file + line, then asks the GitHub integration
                    for that file at that commit --> source in the UI
```

### 3a. What makes it work (all of it is already wired up)

In [`Directory.Build.props`](Directory.Build.props):

| Property | Why |
|---|---|
| `DebugType=portable` | The only symbol format Sentry reads for managed .NET. |
| `PublishRepositoryUrl=true` | Puts the GitHub URL in the PDB instead of a local path. |
| `EmbedUntrackedSources=true` | Generated files still resolve, so no blank frames. |
| `ContinuousIntegrationBuild=true` (CI only) | Normalises paths to `/_/src/...` — a stable stack root for the code mapping. |
| `IncludeSourceRevisionInInformationalVersion` | Version becomes `1.0.0+<sha>`; the app reuses it as the Sentry release. |
| `SentryUploadSymbols` | Uploads the PDBs after every build. |
| `SentryCreateRelease` + `SentrySetCommits` | Ties the release to git commits, which is what lets Sentry pick the right revision on GitHub. |

Source Link itself needs no NuGet package — it has shipped inside the .NET SDK since .NET 8.
`sentry-cli` needs no install either — it comes inside the `Sentry` NuGet package.

The upload block is **off unless `SENTRY_AUTH_TOKEN` is set**, so an ordinary
`dotnet build` never touches the network. `SentryAllowFailure=true` keeps a flaky Sentry
from failing the build; `-p:UseSentryCLI=false` disables the CLI outright.

### 3b. One-time setup in Sentry

1. **Create the project** (platform: .NET) and copy its DSN into `appsettings.json`.
2. **Install the GitHub integration** — Settings → Integrations → GitHub — and add this
   repository to it.
3. **Add a code mapping** — Settings → Integrations → GitHub → Configurations → Code Mappings:

   | Build | Stack Trace Root | Source Code Root |
   |---|---|---|
   | CI (`ContinuousIntegrationBuild=true`) | `/_/` | *(empty — repo root)* |
   | Local Windows build | `C:\path\to\sentry-dotnet-demo\` | *(empty)* |

   Sentry often proposes the mapping automatically once it has seen one event with
   resolved file names — check the frame in the issue for a "Set up code mapping" prompt.
4. **Create an auth token** — Settings → Auth Tokens (an org token with
   `project:releases` + `project:write` is enough).

### 3c. Build with symbol upload, then crash on purpose

```bash
export SENTRY_AUTH_TOKEN=sntrys_xxx
export SENTRY_ORG=your-org
export SENTRY_PROJECT=hello-sentry
dotnet build src/HelloSentry -c Release
dotnet run --project src/HelloSentry -c Release --no-build -- crash
```

Self-hosted Sentry: also `export SENTRY_URL=https://sentry.example.com`.

PowerShell:

```powershell
$env:SENTRY_AUTH_TOKEN="sntrys_xxx"; $env:SENTRY_ORG="your-org"; $env:SENTRY_PROJECT="hello-sentry"
dotnet build src/HelloSentry -c Release
dotnet run --project src/HelloSentry -c Release --no-build -- crash
```

The build prints `Preparing upload to Sentry ... collecting debug symbols`, and the uploaded
PDBs show up under Settings → Projects → (project) → Debug Files.

### 3d. What you should see

Open the new issue. The stack trace should read:

```
PriceCalculator.UnitPrice          src/HelloSentry/Demo/PriceCalculator.cs:10
CheckoutService.Checkout           src/HelloSentry/Demo/CheckoutService.cs:15
Program.Run                        src/HelloSentry/Program.cs:78
```

...with the offending line rendered inline and an **Open in GitHub** link on each frame,
pointing at the commit the binary was built from — not at whatever `main` looks like today.

### 3e. If the repo is private and you would rather not install the integration

Upload the sources to Sentry instead of fetching them from GitHub:

```bash
dotnet build src/HelloSentry -c Release -p:SentryUploadSources=true
```

Or `-p:EmbedAllSources=true` to bake them into the PDB. Use one or the other — the Sentry
targets warn and disable the upload if both are on.

---

## 4. CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds on every push. If the repo has
a `SENTRY_AUTH_TOKEN` secret plus `SENTRY_ORG` / `SENTRY_PROJECT` variables, the same build
uploads symbols and registers the release with its commits; without them it just builds.
`fetch-depth: 0` matters — `sentry-cli` needs real git history to associate commits.

## 5. Troubleshooting

| Symptom | Cause |
|---|---|
| Frames show `<unknown>` / no line numbers | PDBs were never uploaded, or the DLL was rebuilt after the upload (new debug-id). Rebuild **and** re-upload together. |
| File + line resolve, but no GitHub link | Missing code mapping, or the release has no associated commits (`SentrySetCommits`). |
| "Source file not found" on GitHub | The commit is not pushed, or the mapping's stack root does not match the frame path. |
| `The Sentry CLI is not fully configured` | `SENTRY_ORG` / `SENTRY_PROJECT` / `SENTRY_AUTH_TOKEN` not all set. Harmless — upload is skipped and the build succeeds. |
| Build **fails** with `EXEC : error : API request failed` | A token that is set but invalid, or a Sentry server that cannot be reached. `sentry-cli info` writes that to stderr and MSBuild parses it as an error, so it fails the build even though `SentryAllowFailure` is on. Fix the token, or bypass Sentry entirely with `-p:UseSentryCLI=false`. |
| Release shows as `1.0.0` with no `+sha` | Built from a directory with no git repo, so Source Link had no commit to stamp. |

## Layout

```
Directory.Build.props            Source Link + Sentry upload settings (the interesting file)
src/HelloSentry/
  Program.cs                     config loading, SDK init, command dispatch
  appsettings.json               DSN and SentryOptions
  Demo/Greeter.cs                hello world + a wrapped failure
  Demo/NameFormatter.cs          throws ArgumentException
  Demo/CheckoutService.cs        breadcrumb + call into the bug
  Demo/PriceCalculator.cs        the bug: divide by zero
.github/workflows/ci.yml         build + optional symbol upload
```
