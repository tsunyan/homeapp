## Commit messages

`<type>: <summary>` (examples: `feat: add the widget inspector`, `fix: qualify Path against the Shapes namespace`)

- **English, imperative mood, lower case after the colon, no trailing period.**
- One line for the summary, specific enough to be worth reading in `git log --oneline`.
- A scope is optional and rarely needed. `build(deps):` is what Dependabot writes.
- A breaking change takes `!` before the colon — `feat!: ...` — and says what breaks in the body.
- **The body says why, not what.** The diff already shows what changed; the body carries the reason, the
  constraint, or the finding that made the change necessary. Wrap it at about 80 columns.
- **Commits made before this convention existed stay as they are.** Do not rewrite published history to
  retrofit it.

## Branch names

`<type>/<kebab-case-noun-phrase>` (examples: `feat/widget-inspector`, `fix/xaml-markup-pass`, `ci/codeql-csharp`)

The type is the same set both conventions use:

| Type | For |
|---|---|
| `feat` | New behaviour a user can see |
| `fix` | A defect in existing behaviour |
| `docs` | Documentation only |
| `refactor` | A change that alters no behaviour |
| `test` | Tests only |
| `ci` | Workflows, branch protection, repository configuration |
| `build` | The build itself and its dependencies — the type Dependabot writes as `build(deps)` |
| `chore` | Housekeeping that fits none of the above |

- **Use a noun phrase, not a verb phrase.** `feat/widget-inspector`, not `feat/add-widget-inspector`.
  The branch names what the work is about; its commits say what it does.
- **Do not prefix a branch with the name of the agent or person who created it.** Git already records the
  author, and a branch normally outlives whoever opened it — a review and its fixes often land on the branch
  that first carried the implementation.
- Branches Dependabot opens are outside this convention.

`main` is protected: it takes no direct pushes, and every change arrives through a pull request whose
`build`, `secrets` and `analyze` checks pass. See the CI section of [README.md](README.md).

<!-- graft:start -->
## Graft — repo context graph

This repo is indexed in `graft/`: small linked markdown nodes that explain each
system and carry exact file:line spans, kept in sync with the code through git.

Use the graph when locating code, understanding architecture, or assessing a change's
impact. For known-file edits, typos, configuration audits, and tasks unrelated to
indexed code, read the relevant file directly. Use `graft map` only when broad
repository orientation is needed. Choose one query that fits the task; if it returns
unrelated results, use `rg` on the relevant files instead of repeating the query.

- Run `graft ask "<your question>" --source` → ranked nodes with the relevant
  code spans inlined (each hit's ≤8-line crux by default; `--full` for whole
  definitions when the crux isn't enough). Match the tool to the task shape:
  for understanding or editing, verify that the ranked node fits the task; use its
  `covers:` file:line spans and edit straight from `--source`. For
  exhaustive tasks ("every occurrence / every caller of this pattern"), ranked
  results are top-N, not complete — run `graft grep "<literal>"` instead
  (exhaustive over indexed files, grouped by enclosing symbol), falling back
  to `rg` for unindexed files or irrelevant graph results.
- `graft skeleton <file>` → every definition's signature + span, ~10× cheaper
  than reading the file; use it to skim an API surface.
- `graft callers <symbol>` gives precomputed, exact edges — who calls this.
  Add `--direction out` for what it calls, or `--depth N` to walk
  transitively for the full blast radius. For structural questions, skip
  ranking and use this directly.
- Or browse: `graft/INDEX.md` lists every node; follow the links.
- Monorepos and folders of multiple repos rank fairly across sub-projects —
  hits carry `[scope/]` labels naming which one they're from. Narrow with
  `graft ask "<task>" --in <scope>/` once you know where you're working.

When relying on a truncated graph span, read the needed definition before editing
or making claims about it. Prefer focused reads, while allowing direct reads for
known-file tasks and files outside the graph.

After big code changes, refresh the graph with `graft build` (deterministic,
no API key, $0).
<!-- graft:end -->
