# Responsible AI Adoption and Evaluation Framework

## Status

- **Type:** Architecture Lab proposal
- **Horizon:** Medium- to long-term
- **Disposition:** Evaluate incrementally; do not begin with autonomous production decisions
- **Primary source:** [Teaching Engineers, Trusting AI: How Education Enabled Autonomous Code Review](https://www.infoq.com/presentations/duolingo-ai-literacy-code-review/)

## Executive Summary

[The firm] should approach artificial intelligence (AI) adoption as an organizational learning and risk-management program, not primarily as a tool-acquisition exercise.

The recommended progression is:

1. Establish policy, security, privacy, audit, and accountability boundaries.
2. Build practical AI literacy using internal systems and representative tasks.
3. Pilot advisory, reversible use cases with human validation.
4. Measure outcomes using repeatable evaluations rather than adoption statistics alone.
5. Expand autonomy only where failure modes are understood, detectable, and recoverable.

The near-term opportunity is not autonomous code approval. It is using AI to help engineers understand, document, test, and modernize a complex legacy estate while preserving human accountability.

## Context

[The firm] maintains long-lived applications, databases, scheduled processes, integrations, and reporting workflows. These systems may contain substantial undocumented business knowledge and may process sensitive financial, tax, client, or personally identifiable information.

In this environment:

- A small code change can have a large business impact.
- Technical risk cannot be inferred from change size alone.
- AI output may appear authoritative while being incomplete or incorrect.
- Auditability, data handling, and segregation of duties may constrain permissible uses.
- Institutional knowledge is at least as important as source-code context.

Consequently, AI should initially assist engineering judgment rather than replace it.

## Guiding Principles

### AI literacy before AI autonomy

Providing access to a tool does not establish competence. Engineers need practical experience with:

- appropriate and inappropriate use cases;
- context limitations and hallucinations;
- validation techniques;
- secure prompt and data handling;
- model and vendor differences;
- accountability for AI-assisted work.

### Human accountability remains explicit

An engineer remains responsible for submitted code, analysis, and decisions regardless of how much AI contributed. AI-generated explanations or recommendations are evidence to assess, not authority.

### Risk depends on business context

Classification must consider the affected system, data, workflow, permissions, downstream dependencies, and rollback options—not merely line count or apparent code complexity.

### Prefer reversible assistance

Early experiments should produce proposals that humans can inspect, correct, or discard before they affect production.

### Evidence before expansion

Adoption should expand based on demonstrated quality, safety, and value. Tool usage, token consumption, and anecdotal enthusiasm are not sufficient measures of success.

## Recommended Adoption Stages

| Stage | AI role | Human control | Example |
| --- | --- | --- | --- |
| 1 | Explain and summarize | Human verifies all output | Explain a stored procedure or legacy module |
| 2 | Draft artifacts | Human edits and approves | Draft documentation, tests, or migration checklists |
| 3 | Provide recommendations | Human decides whether to act | Code-review comments or dependency warnings |
| 4 | Perform bounded tasks | Human reviews before merge or execution | Implement a small, well-tested refactoring |
| 5 | Make narrowly defined decisions | Policy, automated checks, audit, monitoring, and rollback required | Approve an explicitly whitelisted low-risk change |

Reaching Stage 3 safely could provide substantial value. Autonomous approval should not be treated as the objective or as proof of maturity.

## Candidate Initial Use Cases

The strongest initial candidates combine meaningful effort reduction with low execution risk.

### Legacy-system comprehension

- Explain unfamiliar Java, C#, Visual Basic, and SQL code.
- Trace dependencies among applications, stored procedures, jobs, reports, and integrations.
- Identify likely modernization seams and duplicated business rules.
- Produce initial component and data-flow descriptions for expert validation.

### Documentation and knowledge capture

- Draft system inventories and runbooks.
- Summarize SQL Server Agent jobs and their dependencies.
- Draft data-lineage and interface documentation.
- Convert validated tribal knowledge into maintainable technical documentation.

### Testing and controlled modernization

- Draft characterization tests around existing behavior.
- Suggest edge cases and regression scenarios.
- Compare legacy and proposed implementations.
- Review migration plans for omissions and unsupported assumptions.

### Advisory code and database review

- Flag common correctness, security, and maintainability concerns.
- Explain execution plans or potentially problematic SQL patterns.
- Identify changes involving permissions, sensitive data, or large blast radii for mandatory human review.

## Required Governance Questions

Before broad use, [the firm] should answer:

- Which source code and documents may be submitted to which tools?
- May production, client, account, tax, or personally identifiable data ever enter a prompt?
- How do vendors retain prompts and outputs, and are they used for model training?
- Are interactions auditable and subject to appropriate retention policies?
- Which tools and model configurations are approved?
- How are third-party model and service risks evaluated?
- Who is accountable for AI-assisted code and decisions?
- Which systems or repositories are excluded because of regulatory, contractual, or operational risk?
- What review, testing, monitoring, and rollback controls are required at each adoption stage?

## Evaluation Approach

### Build a firm-specific evaluation set

Preserve representative examples of successful and unsuccessful AI output, such as:

- legacy-code interpretation;
- stored-procedure and job analysis;
- workflow and business-rule explanations;
- security-sensitive changes;
- fabricated tables, interfaces, dependencies, or requirements;
- failures caused by missing repository or business context.

For each case, record the expected characteristics of an acceptable answer. Re-run these cases when evaluating new models, prompts, tools, or configurations.

This converts experience into a regression suite and reduces dependence on impressive but unrepresentative demonstrations.

### Measure outcomes

Candidate measures include:

- time to understand an unfamiliar component;
- documentation accuracy and reviewer corrections;
- percentage of generated tests retained after review;
- defects detected before production;
- code-review turnaround time;
- rework caused by incorrect AI output;
- security or data-handling exceptions;
- cost per accepted result;
- developer confidence and appropriate adoption.

Where practical, compare similar tasks completed with and without AI. Measure accepted outcomes rather than generated volume.

## Guardrails for Increasing Autonomy

Any future autonomous behavior should be:

- explicitly enabled rather than enabled by default;
- limited to named repositories, paths, change types, and experienced owners;
- prohibited for permissions, infrastructure, sensitive data, and regulated workflows unless separately approved;
- backed by deterministic tests and policy checks;
- fully logged and attributable;
- continuously evaluated for false positives, false negatives, defects, and reversions;
- easy to disable and roll back;
- optional when an engineer wants human review for learning, uncertainty, or shared accountability.

## Proposed Architecture Lab Work

### Phase 1: Discovery and policy

- Inventory current AI use, approved tools, and existing restrictions.
- Classify code, documents, and data by sensitivity.
- Document permitted and prohibited scenarios.
- Identify a small cross-functional review group including engineering, security, compliance, legal, and business ownership as appropriate.

### Phase 2: Controlled pilot

- Select one or two advisory use cases involving non-production data.
- Define expected outcomes and failure criteria before starting.
- Assemble an initial evaluation set.
- Run a time-boxed pilot with a small number of experienced engineers.

### Phase 3: Evidence review

- Compare quality, time, cost, rework, and risk with the existing approach.
- Record where missing business or architectural context caused failures.
- Decide whether to stop, revise, repeat, or broaden the pilot.

### Phase 4: Institutionalize successful practices

- Create short, firm-specific labs and examples.
- Publish validated usage patterns and prohibited practices.
- Maintain the evaluation suite and decision log.
- Reassess approved tools and models periodically.

### Phase 5: Consider bounded automation

- Identify repetitive decisions already receiving routine human approval.
- Classify them by business impact and recoverability.
- Introduce automation only for demonstrably low-risk cases with complete guardrails.

## Initial Recommendation

Begin with a narrow pilot focused on legacy-system comprehension, documentation, and characterization-test generation. These activities align with likely modernization needs, preserve human review, and can produce measurable benefits without granting AI operational authority.

Do not initially pursue autonomous code approval. Treat it as a later case study whose prerequisites include mature source control, dependable automated testing, clear code ownership, risk classification, auditability, and sufficient evaluation evidence.

## Decision Criteria

Proceed beyond the pilot only if the evidence shows that AI assistance:

- improves a defined engineering outcome;
- does not create unacceptable security, privacy, compliance, or intellectual-property risk;
- produces output that can be validated economically;
- fits existing accountability and review practices;
- remains supportable if a vendor, model, price, or capability changes.

## Related Reading

The following previously reviewed resources reinforce specific parts of this framework.

### Evaluation and production readiness

- [From AI Agent Demo to Production: Automated Testing and Evaluation](https://www.infoq.com/presentations/ai-agent-testing-evaluation/) — Supports the use of repeatable evaluations, observable behavior, production-like scenarios, and explicit success criteria before trusting an agent in consequential workflows.

### Explicit decisions and bounded autonomy

- [Decision Models in Agentic Architectures: From Production to Agent Skills](https://www.infoq.com/presentations/decision-models-agentic-ai/) — Supports separating deterministic business decisions from probabilistic AI behavior. At [the firm], important policies and eligibility rules should remain explicit, testable, versioned, and auditable rather than being buried in prompts.

### Specifications and validation

- [When Spec-Driven Development Pays off](https://www.infoq.com/articles/when-spec-driven-development-pays-off/) — Supports using clear specifications, acceptance criteria, and feedback loops when AI assists with implementation. This is especially applicable when behavior must be preserved during legacy modernization.

### Skills, mentoring, and engineering judgment

- [How Will We Train Developers If AI Does the Routine Work?](https://www.infoq.com/podcasts/train-developers-ai-routine-work/) — Reinforces the need to preserve deliberate learning, debugging ability, and engineering judgment rather than allowing AI to remove the experiences through which developers build expertise.

### Advisory operational analysis

- [Atlassian Automates Root Cause Analysis by Correlating Metrics, Logs and Traces](https://www.infoq.com/news/2026/09/atlassian-automated-rca/) — Illustrates a potentially valuable advisory use case: correlating operational evidence to propose likely causes while engineers retain responsibility for diagnosis and remediation. This would require sufficiently mature logs, metrics, traces, and correlation identifiers.

## Conclusion

The most appropriate strategy for [the firm] is deliberate enablement rather than either blanket prohibition or indiscriminate adoption. Policy and literacy establish the foundation; advisory use cases build experience; firm-specific evaluations create evidence; and autonomy follows only where risk is bounded and controls are demonstrably effective.
