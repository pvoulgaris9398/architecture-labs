# Architecture Lab Operating Model

## Status

- **Type:** Architecture framework
- **Scope:** Architecture Lab portfolio and decision lifecycle
- **Disposition:** Adopt as the working model; refine as experience accumulates

## Purpose

The Architecture Lab provides a disciplined way to investigate technical opportunities without prematurely converting interesting ideas into approved architecture.

It is intended to help [the firm]:

- connect technical research to business and operational needs;
- make assumptions, risks, and trade-offs explicit;
- evaluate alternatives using representative evidence;
- favor incremental and reversible change;
- preserve architectural knowledge and decision history;
- distinguish exploration from organizational commitment.

The lab is not a parallel delivery organization, an unbounded research program, or a mechanism for bypassing normal ownership, security, compliance, or change controls.

## Core Principles

### Begin with the problem

Every investigation should state the problem, affected stakeholders, current constraints, and desired outcome before considering products or technologies.

### Connect architecture to business value

Technical elegance is insufficient justification. A proposal should improve a meaningful outcome such as reliability, control, delivery speed, maintainability, auditability, cost, security, or business responsiveness.

### Separate evidence from assumptions

Documents should distinguish:

- observed facts about the current environment;
- stakeholder requirements;
- working assumptions;
- hypotheses requiring validation;
- recommendations supported by evidence.

### Prefer incremental and reversible change

Experiments should be small enough to stop, revise, or replace without creating an unintended production commitment. Large transformations should be decomposed into independently valuable steps.

### Preserve optionality without avoiding decisions

Vendor neutrality and abstraction are useful when they address a credible risk. They should not create generalized frameworks for hypothetical futures. Decisions should be made at the last responsible moment, then recorded clearly.

### Treat operational concerns as architecture

Security, privacy, audit, deployment, observability, recovery, ownership, and supportability are part of the design—not post-implementation additions.

### Prefer the simplest sufficient solution

The lab should actively test whether an existing platform, modest process change, or small component can solve the problem before recommending a new platform or architectural style.

## Repository Roles

The repository separates reusable guidance, candidate changes, and accepted decisions.

| Location | Purpose | Typical contents |
| --- | --- | --- |
| `docs/frameworks` | Reusable reasoning structures and evaluation criteria | Adoption frameworks, workload classifications, operating models |
| `docs/proposals` | Context-specific recommendations not yet accepted | Modernization proposals, technology evaluations, pilot proposals |
| `docs/decisions` | Accepted architectural decisions and their consequences | Architecture Decision Records (ADRs) |

Frameworks guide analysis. Proposals apply those frameworks to a particular problem. Decisions record an approved choice. Moving from one category to another requires additional evidence and authorization; it is not merely a change of filename.

## Investigation Lifecycle

### 1. Intake

Capture the trigger for investigation:

- a business or operational problem;
- recurring delivery friction;
- material risk;
- upcoming platform or vendor change;
- modernization opportunity;
- relevant external case study or technology.

The intake should identify why the subject merits attention now. Interesting technology without a plausible problem may be retained as a reference but should not automatically become a proposal.

### 2. Discovery

Establish the current state using code, configuration, operational data, documentation, and stakeholder knowledge.

Discovery should answer:

- What capability or workflow exists today?
- Who owns and depends on it?
- What business outcome does it support?
- Where are the actual constraints and failure modes?
- Which requirements are regulatory, contractual, operational, or historical?
- What evidence is missing?

### 3. Hypothesis

State a falsifiable improvement hypothesis. For example:

> If orchestration state is made explicit and durable, then failed nightly processes can resume safely with less manual diagnosis and reprocessing.

The hypothesis should define the anticipated benefit and the conditions under which the idea should be rejected.

### 4. Options and trade-offs

Evaluate the current approach alongside credible alternatives. Include the option to make no architectural change.

Compare options using relevant dimensions such as:

- functional fit;
- operational complexity;
- security and compliance;
- reliability and recoverability;
- integration with the existing estate;
- migration effort;
- skills and support model;
- vendor dependence;
- total cost;
- reversibility.

### 5. Experiment

Design the smallest safe experiment capable of testing the hypothesis.

An experiment should have:

- a named owner;
- a fixed scope and duration;
- representative inputs and constraints;
- explicit success and failure criteria;
- no unapproved production dependency;
- an exit and cleanup plan;
- recorded results, including negative findings.

### 6. Evidence review

Compare the results with the original hypothesis and baseline. Identify limitations, unexpected costs, and conditions that may not generalize.

The review should recommend one of the following:

- proceed to a proposal;
- repeat with revised scope;
- defer pending a named prerequisite;
- reject and document why;
- retain as reference without further action.

### 7. Proposal

A proposal applies the evidence to a concrete context at [the firm]. It should describe:

- the recommended change;
- affected systems and stakeholders;
- migration and coexistence approach;
- risks and mitigations;
- operational ownership;
- estimated effort and cost;
- unresolved questions;
- required approvals.

### 8. Decision

Consequential accepted choices should be recorded as ADRs under `docs/decisions`.

An ADR should capture:

- context;
- decision;
- alternatives considered;
- consequences and trade-offs;
- implementation constraints;
- conditions that would justify revisiting the decision.

### 9. Follow-through

After implementation, compare actual outcomes with the proposal. Update the relevant framework when experience reveals a reusable lesson, but do not rewrite historical ADRs to imply that the original decision had knowledge acquired later.

## Status Model

| Status | Meaning |
| --- | --- |
| `candidate` | Potentially relevant; not yet investigated |
| `discovering` | Current state and requirements are being established |
| `evaluating` | Options or a hypothesis are being tested |
| `proposed` | A supported recommendation is awaiting a decision |
| `accepted` | Authorized for implementation |
| `adopted` | Implemented and operating |
| `deferred` | Valid subject, but a named prerequisite or priority blocks progress |
| `rejected` | Evaluated and deliberately not pursued |
| `superseded` | Replaced by a later framework, proposal, or decision |

Every deferred or rejected item should include a short rationale. Deferred items should identify the trigger for reconsideration rather than relying on an arbitrary review date.

## Evidence Standards

Evidence should be proportionate to the consequence of the decision.

### Acceptable evidence may include

- production and operational measurements;
- representative prototypes;
- failure and recovery tests;
- source-code and dependency analysis;
- security, audit, or compliance review;
- user or operator feedback;
- vendor documentation and contractual commitments;
- external case studies with explicitly assessed differences.

### Insufficient evidence on its own

- vendor demonstrations;
- popularity or industry momentum;
- benchmark results from unrelated workloads;
- anecdotal productivity claims;
- generated code that has not been reviewed and tested;
- adoption or usage counts without outcome measures.

## Decision Significance

Not every change requires an ADR or lab investigation.

Use an ADR when a decision:

- affects multiple systems or teams;
- introduces a durable platform or vendor commitment;
- materially changes security, data, or operational risk;
- is expensive to reverse;
- establishes a precedent others will follow;
- involves meaningful disagreement or non-obvious trade-offs.

Routine implementation choices can remain within normal engineering review when they do not create broader constraints.

## Living-Document Policy

Frameworks are curated living documents. They may be updated when new experience or research introduces:

- a materially new principle;
- a newly relevant use case;
- a significant risk or failure mode;
- a better evaluation method;
- evidence that changes an existing recommendation.

Supporting evidence may be added as related reading when it materially strengthens the framework. Repetitive, speculative, or tangential material should not be added merely because it is interesting.

Substantial changes to a framework should note the reason in source-control history. Changes to an accepted decision should normally produce a new ADR that supersedes the earlier one.

## Initial Topic Portfolio

The initial portfolio may include:

- artificial intelligence adoption and engineering effectiveness;
- workload classification and data-platform modernization;
- workflow, batch processing, and orchestration;
- legacy-application modernization;
- source control and delivery modernization;
- observability and operational resilience;
- reporting and analytical architecture.

These are topic anchors, not commitments to create a separate document or initiative for each one. New artifacts should be created only when sufficient evidence or practical need exists.

## Review Checklist

Before advancing an investigation, confirm:

- [ ] The business or operational problem is explicit.
- [ ] Current-state facts are separated from assumptions.
- [ ] Owners, users, and affected systems are identified.
- [ ] The existing approach and no-change option were considered.
- [ ] Security, compliance, audit, and operational concerns were included.
- [ ] Success and failure criteria are measurable.
- [ ] The experiment is bounded and reversible.
- [ ] Migration, coexistence, rollback, and support were considered.
- [ ] The recommendation follows from evidence.
- [ ] A proposal or ADR is created only when warranted.

## Conclusion

The Architecture Lab should enable deliberate learning without creating architecture by enthusiasm. Its output is not merely a collection of technology notes; it is a traceable progression from problem and evidence to recommendation and decision.

