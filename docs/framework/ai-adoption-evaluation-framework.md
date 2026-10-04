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

The effectiveness of that assistance will depend on whether the software estate makes its architecture, constraints, conventions, and intent explicit enough for both human engineers and AI development agents to navigate safely.

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

### Preserve learning and independent judgment

AI-assisted work should build the team's ability to understand, challenge, and operate the system. Completing an artifact does not by itself demonstrate that an engineer has acquired the knowledge needed to validate it or handle an unfamiliar failure.

For selected learning tasks, engineers should form an initial explanation or predict an outcome before consulting AI, compare that reasoning with the generated analysis, and resolve disagreements using source code, execution evidence, and knowledgeable colleagues. Revisit the material later and use different forms of evidence, such as code, dependency diagrams, logs, and recovery exercises.

Apply this selectively: a small representative sample can support deliberate learning while AI accelerates broader inventory and drafting. Managers should allow time for investigation and knowledge transfer. At [the firm], an experienced engineer's learning objectives may center on local business rules, undocumented dependencies, and SQL Server operations rather than general programming fundamentals.

### Human accountability remains explicit

An engineer remains responsible for submitted code, analysis, and decisions regardless of how much AI contributed. AI-generated explanations or recommendations are evidence to assess, not authority.

### Risk depends on business context

Classification must consider the affected system, data, workflow, permissions, downstream dependencies, and rollback options—not merely line count or apparent code complexity.

### Prefer reversible assistance

Early experiments should produce proposals that humans can inspect, correct, or discard before they affect production.

### Make execution permissions explicit and enforceable

When an AI agent can execute commands, modify files, run tests, or call services, its permitted access should be a versioned, reviewable engineering artifact. Record the allowed tools, filesystem locations, network destinations, credentials, persistence, and resource limits for the task. The agent must not be able to widen its own permissions.

Distinguish requested permissions from host-approved grants and runtime enforcement. A declaration or prompt alone does not establish a security boundary. Verify that the chosen environment blocks prohibited access, and review permission expansion separately from routine software updates. Record the effective grants and environment version with each execution.

Isolation, data handling, and correctness remain separate concerns. A sandbox does not authorize sending source material to a model provider, validate generated SQL, or make actions against an accessible external service harmless. Continue to apply approved data handling, human review, and deterministic validation.

### Evidence before expansion

Adoption should expand based on demonstrated quality, safety, and value. Tool usage, token consumption, and anecdotal enthusiasm are not sufficient measures of success.

### Treat AI as an architectural stakeholder

An AI development agent may act as both a consumer of existing components and a future modifier of the system. The design should therefore help it—and the humans reviewing its work—to:

- discover and reuse approved capabilities rather than recreate them;
- locate the correct implementation point for a requirement;
- understand component responsibilities, dependencies, and prohibited interactions;
- identify the likely blast radius of a change;
- trace requirements through implementation and tests;
- distinguish documented facts and constraints from unsupported inference.

This does not give AI authority over architecture. It recognizes that explicit, current design information is a prerequisite for safe assistance. Undocumented conventions and tribal knowledge increase the probability of locally plausible but systemically inconsistent changes.

### Preserve intent continuity, not merely conversation history

Longer context windows and searchable conversation logs do not ensure that an AI development agent will apply an earlier requirement to a later task. The agent must be able to determine which prior decisions are relevant, still valid, and applicable to the present component and scope.

Durable requirements and decisions should therefore record enough structure to support:

- retrieval of potentially relevant constraints;
- verification that each constraint applies to the current task;
- explicit scope, such as component, environment, workflow, or change class;
- supersession when a later decision replaces an earlier one;
- coexistence when apparently conflicting decisions apply to different scopes;
- traceability from the active constraint to implementation and validation evidence.

Conversation history is an audit trail, not automatically an effective model of current intent. For consequential work, the agent should operate from a task-specific projection of active constraints assembled from maintained sources such as architecture decision records (ADRs), specifications, policies, repository guidance, and acceptance criteria. Retrieval should identify candidates; it should not decide validity by itself.

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

## Proposed Experiments: Code Review, Legacy Analysis, and Classification

Added October 4, 2026. These are proposed experiments for [the firm], not capabilities demonstrated or validated by the source article.

### Experiment 1: Advisory code review

Use AI to review a small set of historical changes in SQL, C#, or Java. Provide the relevant requirements, surrounding code, and known constraints. Ask for evidence-backed correctness, security, maintainability, and regression concerns, with references to the affected code.

Compare the findings with human review and known defects. Measure useful findings, missed defects, false alarms, review effort, and cost per accepted finding. Start with historical or non-production changes; engineers retain responsibility for conclusions and approval.

### Experiment 2: Legacy-system analysis, prioritizing SQL Server Agent jobs

Review the SQL Server Agent job ecosystem at [the firm] to understand its behavior and identify practical improvements. The initial result should be a validated inventory, dependency map, and prioritized improvement backlog. No assumption is made that the current ecosystem has the problems listed below.

#### Evidence to collect

Start with an approved, read-only export of representative job definitions, steps, schedules, execution history, and referenced code. Include, where available:

- Job identifiers, names, enabled status, owners, business purposes, and accountable business and technical contacts.
- Step order, execution subsystem, target database, commands, success and failure actions, retry settings, and execution identity or proxy references.
- Schedules, expected processing windows, duration trends, failure history, and alert or notification configuration.
- Referenced stored procedures, scripts, packages, files, linked servers, and external systems.
- Existing runbooks, service expectations, known dependencies, and recovery procedures.

Remove credentials and other restricted information before submitting material to an approved AI tool. Record the export time, environment, and missing evidence. Definitions and history alone may not expose dependencies implemented in external scripts, dynamic SQL, application code, or manual procedures.

#### Questions for the review

- What business process does each job implement, and which inputs and outputs does it depend on?
- Which dependencies are explicit, and which appear to rely on schedule timing, files arriving, or undocumented operating knowledge?
- Are failure paths and notifications consistent with the actual business outcome? Could an earlier failure be obscured by a later successful step?
- Are retries appropriate for the failure type and safe with respect to duplicate processing and partial commits?
- Can processing resume from a failed step or record, or does recovery require rerunning successful work?
- Where would batch identity, correlation identifiers, row counts, reconciliation, or clearer logging improve diagnosis and recovery?
- Are duplicated steps, unclear ownership, obsolete candidates, unnecessary polling, overlapping schedules, or long-running work worth investigating?
- Which business logic could become a reusable process with explicit inputs, outputs, and execution contracts?
- Which changes could improve the existing SQL Server Agent arrangement without introducing a new orchestration platform?

Treat overlap and long duration as investigation signals, not proof of contention or inefficient SQL. Validate performance recommendations with appropriate runtime evidence. Confirm business purpose and downstream consumers before recommending retirement or consolidation.

#### Reviewable outputs and evaluation

Produce a job catalog, an evidence-backed dependency map, and a short backlog. For each finding, record the job and step, observed evidence, business impact, proposed improvement, uncertainty, verification needed, effort estimate, and acceptance criteria. Mark dependencies as confirmed, inferred, or unknown; a plausible inference must not become an asserted fact.

Validate an initial sample with the incumbent engineers and relevant business owners, especially while institutional knowledge remains available. Measure inventory accuracy, dependency omissions, accepted recommendations, reviewer corrections, time saved, and investigation effort. Prioritize changes by business criticality and recovery difficulty as well as implementation effort.

This experiment aligns with restartable, auditable, composable batch processing: it can reveal where existing jobs would benefit from immutable input batches, explicit execution state, preserved attempt history, and controlled recovery. Those are candidate improvements to assess against actual evidence, not prerequisites imposed on every job.

#### Deliberate learning within the job review

Choose a small representative sample of jobs for deeper investigation. Before reviewing AI output, have the engineer explain each job's inputs, changes, outputs, dependencies, and expected behavior if a step fails. Compare that explanation with the AI analysis, then resolve gaps through source inspection, execution evidence, and incumbent knowledge. Capture the verified reasoning and any remaining uncertainties.

Revisit the sample later with a different scenario, such as late input, a partial commit, or a rerun after failure. Check whether the engineer can independently explain the consequences, identify missing evidence, and describe safe recovery. Use these exercises to assess knowledge transfer, not to require manual analysis of every job before AI assistance.

### Experiment 3: Classification and advisory triage

Classification assigns an input to one of a predefined set of categories. AI may help interpret varied language and incomplete context; known error codes and explicit validation rules should continue to use deterministic logic where sufficient.

| Input | Candidate categories | Advisory purpose |
| --- | --- | --- |
| Historical processing failure | Source unavailable; invalid data; configuration issue; unknown or needs review | Suggest investigation routing |
| Support request | Reporting; access; data correction; application defect; unknown or needs review | Suggest the responsible area |
| Stored procedure and dependencies | System of record; transient cache; workflow state; reporting model; integration staging; mixed or unclear | Assist SQL-estate documentation |
| Vendor release note | Relevant to current configuration; potentially relevant; unrelated; needs review | Assist manual screening |

Start with historical processing failures. Supply the error message, relevant log entries, step description, and human-reviewed category definitions. Ask for a suggested category and supporting evidence when the chosen tool supports explanation. Evaluate any constrained-choice service separately from an explanatory model or workflow; do not assume the service returns explanations or calibrated confidence.

A predefined answer list constrains output format, not correctness. Preserve an explicit unknown or needs-review outcome. Separate observed symptoms from inferred causes, and allow mixed classifications where a component has multiple responsibilities.

Evaluate against human-reviewed examples, including ambiguous cases and uncommon but consequential failures. Track precision and recall by category, abstention frequency, incorrect high-impact routing, reviewer effort, and cost per accepted result. Compare with a simple rules-based baseline and repeat evaluation when models, prompts, or category definitions change.

Classification may supplement ETL diagnosis and triage. It must not silently determine record validity, mark a failed step successful, authorize corrections, or establish retry eligibility. Those decisions remain explicit, testable business logic.

### Platform developments to monitor

The [OpenAI DevDay 2026 recap](https://www.infoq.com/news/2026/10/openai-devday-2026/) reports lower-cost models, managed agent execution and computer use, cloud coding environments, expanded plugins, persistent agents, shared workspaces, and a limited-preview Decisions API for predefined-choice tasks.

These announcements motivate evaluation; they do not establish production reliability, suitability, or total operating cost at [the firm]. Select tools according to approved data handling, measurable task performance, auditability, permissions, and supportability. Prefer direct, read-only exports for the SQL Server Agent assessment where available; graphical computer use is not required for that experiment.

## AI-Readiness Assessment

Before allowing an AI development agent to modify a repository or system, assess whether the environment provides enough explicit context and independently verifiable constraints. The objective is not to maximize documentation. It is to supply the minimum reliable context needed to navigate, change, and validate the system safely.

| Dimension | Assessment questions | Useful evidence |
| --- | --- | --- |
| Architecture | Are system boundaries, responsibilities, dependencies, and approved interaction patterns explicit? | Current diagrams, architecture descriptions, dependency rules, architecture decision records (ADRs) |
| Domain intent | Are important terms, invariants, workflows, and business rules documented close to their implementation? | Domain glossary, executable rules, examples, acceptance criteria |
| Discoverability | Can an agent locate the correct component and existing capability before generating new code? | Repository map, naming conventions, searchable interface documentation, component ownership |
| Change containment | Are modules and interfaces designed so that a bounded change has a bounded impact? | Clear contracts, dependency direction, encapsulation, impact-analysis tooling |
| Verification | Can correctness be demonstrated without relying primarily on human intuition? | Characterization, unit, integration, contract, regression, and policy tests |
| Operational behavior | Are failure modes, telemetry, recovery procedures, and side effects visible? | Logs, metrics, traces, correlation identifiers, runbooks, idempotency and retry rules |
| Constraints | Are security, privacy, compliance, data-handling, and repository restrictions machine-discoverable where practical? | Policy-as-code, protected paths, approved-tool configuration, automated checks |
| Intent continuity | Can the system determine which earlier requirements and decisions remain active and applicable to the current task? | Scoped decision records, supersession metadata, current specifications, task-specific active-constraint summaries |
| Design currency | Does documented design describe the actual system, and is it updated when consequential changes are made? | Reviewable documentation changes, ADRs, drift checks, ownership and review dates |

### Readiness interpretation

- **Ready for assistance:** The agent can explain or draft artifacts, but humans must supply missing context and verify all conclusions.
- **Ready for bounded implementation:** The change area is discoverable and contained, expectations are explicit, and deterministic checks can validate the result before merge.
- **Ready for delegated decisions:** The permitted decision class, constraints, evidence, escalation conditions, audit trail, and rollback behavior are enforced by the delivery platform.
- **Not ready:** Critical intent remains tribal, dependencies or blast radius cannot be determined economically, or correctness cannot be independently verified.

The assessment should be applied per repository, subsystem, and task class—not used as a single maturity score for the organization. A system may be ready for documentation assistance while remaining unsuitable for AI-generated production changes.

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
- failures caused by missing repository or business context;
- failures caused by retrieving an obsolete, superseded, or incorrectly scoped requirement;
- failures caused by retaining a valid requirement but not applying it to a later task.

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

### Evaluate learning and independent understanding

Define a learning objective alongside the delivery objective for selected pilot tasks. For the SQL Server Agent review, this may include understanding local business purpose, dependency ordering, transaction boundaries, and recovery behavior.

Assess whether engineers can:

- Explain a sampled workflow and justify its dependencies using evidence.
- Detect and correct an unsupported AI claim or an omitted dependency.
- Predict the consequences of an unfamiliar failure or rerun scenario.
- Locate the source evidence and distinguish confirmed behavior from assumptions.
- Retain and apply that understanding in a later session.

Compare the engineer's initial and later explanations, recording substantive corrections and unresolved gaps. Track whether another engineer can use the validated documentation to investigate a new scenario. These are proposed practical assessment measures, not a validated learning benchmark. Evaluate them alongside delivery time, accepted quality, review effort, and cost; do not equate documentation volume or faster completion with improved understanding.

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

### Optional pilot: A bounded executable-agent environment

Added October 4, 2026. Evaluate a repeatable analysis environment before introducing a broader agent platform. Docker Sandbox Kits are one candidate implementation; the requirements below are independent of that product.

| Task | Proposed access boundary | Reviewable result |
| --- | --- | --- |
| SQL Server Agent ecosystem review | Read-only sanitized exports of job definitions, schedules, history, and referenced code; separate writable output directory; no production database connection | Inventory, dependency map, evidence references, and improvement backlog |
| Legacy-code analysis or refactoring | Selected repository copy, approved build tools, local tests, and narrowly permitted network access | Analysis, test results, and reviewable changes |
| Restartable ETL lab | Disposable test database, synthetic input batches, and access limited to lab resources | Failure/recovery observations, proposed SQL, and validation evidence |

#### Initial scope and controls

Start with the SQL Server Agent export-analysis task. Allow the agent to run analysis scripts and produce artifacts while enforcing read-only access to the input evidence. Permit only the model endpoint and other destinations explicitly needed by the approved tool configuration. Supply task-scoped credentials where needed; do not inherit broad workstation credentials, host directories, or control of the host container runtime.

Record the environment or image version, tool versions, model configuration, input snapshot identity, requested permissions, effective grants, executed commands, and output artifacts. Where using images, pin immutable digests so that the reviewed environment can be identified precisely. Logs should omit secret values. Retain outputs for human assessment; no production changes are part of this pilot.

#### Verify the boundary and assess value

Before using firm material, run harmless checks with synthetic fixtures to confirm that the agent cannot modify protected inputs, reach an unapproved endpoint, access an unrelated directory, or obtain an ungranted credential. Confirm that authorized analysis and report generation succeed. Test the update workflow with a deliberately expanded permission request and verify that it is held for review. Do not assume that a packaged policy is enforced simply because the image runs successfully.

Evaluate setup and maintenance effort, analysis quality, repeatability, execution visibility, and whether the boundary tests behave as expected. Compare the environment with a simpler restricted workstation or virtual machine arrangement. Expand only if it provides useful execution capability and demonstrable control at a supportable cost for the small team.

#### Docker implementation considerations

Docker's [Sandbox Kit specification announcement](https://www.docker.com/blog/docker-sandbox-kit-spec/) describes an ordinary Open Container Initiative (OCI) image carrying the agent, tools, and typed permission requests. A conforming runtime enforces those requests subject to host approval; the annotation is inert without that runtime. Docker describes micro virtual machine isolation for Docker Sandboxes, proxy-managed credentials for supported requests, and permission-expansion detection for runtimes that gate updates.

The [InfoQ recap](https://www.infoq.com/news/2026/10/docker-sandbox-ai-agent/) reports that cross-runtime portability has not yet been demonstrated and does not establish confirmed Cloud Native Computing Foundation (CNCF) program acceptance or maturity. Treat the implementation as a candidate to evaluate. Confirm current platform support, licensing, networking behavior, available audit evidence, and suitability for the actual workstation and lab environment before selecting it.

## Initial Recommendation

Begin with a narrow pilot focused on legacy-system comprehension, documentation, and characterization-test generation. Prioritize a read-only review of the SQL Server Agent job ecosystem, with advisory code review and historical-failure classification as additional bounded experiments. These activities align with likely modernization needs, preserve human review, and can produce measurable benefits without granting AI operational authority.

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

### Explicit execution permissions and sandbox enforcement

- [Docker Sandbox Kit Spec: Packaging AI Agent Permissions as OCI Images](https://www.infoq.com/news/2026/10/docker-sandbox-ai-agent/) — Motivates packaging agent access requests as reviewable artifacts. The proposed lab applies this idea to bounded execution while retaining separate checks for data handling and correctness.
- [From Dockerfile to Kit: the Docker Sandboxes Kit Specification](https://www.docker.com/blog/docker-sandbox-kit-spec/) — Primary-source description of Kit declarations, runtime enforcement, isolation, and permission-expansion review. Product claims should be verified in the selected environment.

### Deliberate learning during AI-assisted work

- [How to Develop Software Engineering Skills in the Age of AI](https://www.infoq.com/news/2026/10/software-engineering-skills/) — Reports Chelsea Troy's distinction between accelerating task completion and developing understanding. Her discussion of prediction, spaced practice, varied formats, and management support informs the learning principle above. The SQL Server Agent exercise and assessment measures are adaptations proposed for [the firm], not experiments demonstrated by the article.

### AI-ready software design

- [Software Design in the Age of AI](https://towardsdatascience.com/software-design-in-the-age-of-ai/) — Frames AI development agents as consumers and future modifiers of software. It reinforces the need for explicit, current design; discoverable reusable components; navigable dependencies; contained change impact; and traceability from requirements to code and tests.

### Intent continuity and agent memory

- [Coding Agents Don’t Need Longer History—They Need Intent Continuity](https://towardsdatascience.com/coding-agents-dont-need-longer-history-they-need-intent-continuity/) — Demonstrates a small structured-memory implementation that separates retrieval from verification and models scope and supersession explicitly. Its synthetic evaluation suggests that task-specific verification can recover requirements missed by keyword retrieval, although the small, schema-aligned benchmark should be treated as illustrative rather than general proof. The practical pattern is an **active decision projection**: derive the currently applicable constraints before an agent plans or changes code, then verify the result against them.

## Conclusion

The most appropriate strategy for [the firm] is deliberate enablement rather than either blanket prohibition or indiscriminate adoption. Policy and literacy establish the foundation; advisory use cases build experience; firm-specific evaluations create evidence; and autonomy follows only where risk is bounded and controls are demonstrably effective.
