# Issue standard

Use this standard when creating, rewriting, splitting or triaging an issue, including through the API
or CLI. Start with the [shared issue template](https://github.com/IgnyteSoftware/inquiry/issues/new?template=work-item.md).
The template source is [work-item.md](https://github.com/IgnyteSoftware/inquiry/blob/main/.github/ISSUE_TEMPLATE/work-item.md).

## Title

Use `Area: action or behavior` in sentence case, for example:

- `Transactions: Bind handle operations to their owned transaction`
- `Documentation: Make the getting-started example executable`
- `Benchmarks: Fail comparisons when required measurements are missing`

Name the affected product area and the required change. Keep workflow provenance, reviewer identities,
priority and release numbers out of the title; labels and the milestone carry classification.

## Body

Keep these six level-two headings in this order for every issue. Replace the template instructions
with concrete content. Use "No separate dependency identified" when Related work has no entries.

| Heading             | Required content                                                                                                                                                   |
| ------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Summary             | Current behavior or unmet need, with the user-visible effect.                                                                                                      |
| Scope               | Expected behavior, affected providers or APIs, and relevant exclusions.                                                                                            |
| Acceptance criteria | Observable outcomes as task-list items. Check an item only when its outcome is verified.                                                                           |
| Evidence            | Version or commit, provider/framework where relevant, reproduction or consumer evidence, expected/actual results, and source links. State what remains unverified. |
| Release decision    | Milestone or Unscheduled, with the reason and any explicit fix-or-accept decision.                                                                                 |
| Related work        | Dependencies, duplicates and split follow-ups linked by issue number.                                                                                              |

Keep the body focused on the product. Omit reviewer identities and workflow provenance. Preserve useful
reproductions, source references and consumer constraints when rewriting an issue. Distinguish an
observed defect from a source-based risk or missing test evidence.

## Labels

Each issue has exactly one label from each family below. Use the repository's existing vocabulary;
add a new area only when none fits. Replace older free-form classifications when triaging an issue.

| Family      | Values                                                                                                         | Meaning                                                                                                                      |
| ----------- | -------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `type:`     | `bug`, `feature`, `documentation`, `test`, `performance`, `maintenance`, `release`, `decision`                 | The primary kind of work.                                                                                                    |
| `area:`     | `api`, `benchmarks`, `docs`, `generator`, `providers`, `release`, `runtime`, `telemetry`, `testing`, `tooling` | The primary affected component.                                                                                              |
| `priority:` | `P0`, `P1`, `P2`, `P3`                                                                                         | Urgent critical impact; high-priority release work; normal planned work or decision; low-priority improvement, respectively. |

The template starts with `type:decision`, `area:triage` and `priority:P2`. Maintainers select the actual
type, area and priority before the issue is ready for implementation. `area:triage` is temporary.
Record duplicates in Related work and the closure reason rather than adding another label family.

## Milestones and cleanup

Use the milestone to state release scope independently of priority. Put correctness, public-contract
and required evidence work for the first stable release in **1.0.0**. Put accepted follow-up work in
**1.1.0**; leave demand-driven proposals unscheduled. A pre-freeze design decision belongs in 1.0.0
even when its outcome may be an explicit acceptance of the current contract.

Before creating an issue, search open and recently closed issues for the same behavior. Extend the
existing issue when the scope matches. Split independently actionable concerns and link both sides.
When only a narrow fix blocks release, give that fix its own 1.0.0 issue and retain the larger feature
in its later milestone.

For cleanup, preserve issue numbers, assignees and historical comments. Update stale claims in the
body. Consolidate duplicates only after their destination includes the source evidence and acceptance
criteria. Record a reason when closing an obsolete or intentional-behavior report; consolidation does
not mean the implementation is fixed.

After writing, read the issue back and verify its title, six headings, three labels, milestone and
links. Check that release wording agrees with the milestone and that no unverified work is marked done.
