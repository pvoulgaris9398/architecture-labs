# SQL Server Parameter Sensitive Plan Optimization Lab

## Overview

SQL Server parameter sniffing is not inherently a defect.

When SQL Server compiles a parameterized query, it can inspect the current parameter value and use that value when estimating cardinality and choosing an execution plan. This is often desirable because the optimizer can select a plan appropriate for the data being requested.

The problem arises when:

1. The underlying data distribution is highly skewed.
2. Different parameter values legitimately require different physical execution strategies.
3. SQL Server compiles a plan for one parameter value.
4. That cached plan is subsequently reused for a radically different parameter value.

A plan that is excellent for one population can therefore be disastrous for another.

SQL Server 2022 introduced **Parameter Sensitive Plan (PSP) optimization** to address an important subset of this problem. Instead of requiring a single cached execution plan for every parameter value, SQL Server can create a **dispatcher plan** that routes parameter values into different cardinality ranges and maintains separate **query variants** for those ranges.

The purpose of this lab is to make this behavior directly observable.

---

## Core Scenario

Consider an `Orders` table with a deliberately skewed distribution:

| Customer type | Customers | Orders per customer |
| --- | ---: | ---: |
| Enterprise outlier | 1 | 400,000 |
| Ordinary customer | ~6,000 | 100 |

Assume an index exists on:

```sql
CustomerID
```

but the index does not cover all columns selected by the query.

For an ordinary customer returning approximately 100 rows, SQL Server may reasonably choose:

```text
Index Seek
    ↓
Key Lookup
```

For the enterprise customer returning approximately 400,000 rows, hundreds of thousands of key lookups may be dramatically more expensive than:

```text
Clustered Index Scan
```

Neither plan is inherently wrong.

The important observation is:

> Different parameter populations legitimately require different execution plans.

That is the class of problem PSP is designed to address.

---

# Experiment 1: Traditional Parameter Sniffing

Start with:

```text
SQL Server 2022+
Database compatibility level 150
PSP unavailable
```

Compile the stored procedure using a small customer:

```sql
EXEC dbo.usp_GetCustomerOrders @CustomerID = 2;
```

Then execute it for the large customer:

```sql
EXEC dbo.usp_GetCustomerOrders @CustomerID = 1;
```

Expected behavior:

```text
Compile value = 2
Estimated rows ≈ 100

          ↓

Index Seek
+
Key Lookup

          ↓

Plan cached

          ↓

Execute value = 1
Actual rows ≈ 400,000

          ↓

Same Seek + Lookup plan reused
```

This gives us the classic parameter-sensitive plan problem.

The interesting evidence is not merely execution time. Inspect:

- compiled parameter value
- runtime parameter value
- estimated rows
- actual rows
- physical operators
- logical reads
- CPU time
- elapsed time

A particularly useful observation should be the cardinality mismatch:

```text
Estimated rows ≈ 100
Actual rows    ≈ 400,000
```

The query plan was reasonable for the value for which it was compiled.

It became unreasonable when reused.

---

# Experiment 2: Reverse the Compilation Order

Clear the database procedure cache and execute the large customer first:

```sql
EXEC dbo.usp_GetCustomerOrders @CustomerID = 1;
```

Then execute a small customer:

```sql
EXEC dbo.usp_GetCustomerOrders @CustomerID = 2;
```

The expected behavior becomes approximately:

```text
Compile value = 1
Estimated rows ≈ 400,000

          ↓

Clustered Index Scan

          ↓

Plan cached

          ↓

Execute value = 2
Actual rows ≈ 100

          ↓

Same scan reused
```

This demonstrates the so-called **compilation lottery**.

The first parameter used during compilation can determine the execution strategy subsequently reused by very different parameter populations.

The lesson is important:

> The problem is not simply that SQL Server selected a bad plan. The problem is that one reusable plan cannot adequately represent the entire parameter population.

---

# Experiment 3: Enable Parameter Sensitive Plan Optimization

Change the database compatibility level to 160:

```sql
ALTER DATABASE PSPDemo
SET COMPATIBILITY_LEVEL = 160;
```

SQL Server 2022 PSP is enabled by default when the database is using compatibility level 160, assuming the query is otherwise eligible.

Now repeat executions using both small and large customers.

Conceptually, SQL Server can produce:

```text
Parameterized Query
        │
        ▼
   Dispatcher Plan
        │
   ┌────┴────┐
   │         │
   ▼         ▼
Small       Large
bucket      bucket
   │         │
   ▼         ▼
Variant A   Variant B
   │         │
   ▼         ▼
Seek +      Clustered
Lookup      Scan
```

Rather than recompiling the query for every execution, SQL Server preserves plan reuse while acknowledging that **one reusable plan is insufficient**.

The dispatcher evaluates the parameter value and routes the execution to an appropriate query variant.

Each query variant can have its own cached execution plan.

---

# PSP and Statistics

PSP is not independent of SQL Server statistics.

During compilation, SQL Server examines column statistics and their histograms to identify significant non-uniform distributions.

The optimizer determines cardinality boundaries and uses those boundaries to create parameter ranges.

Conceptually:

```text
Data distribution
       ↓
Statistics histogram
       ↓
Cardinality estimates
       ↓
PSP bucket boundaries
       ↓
Dispatcher
       ↓
Query variant
       ↓
Execution plan
```

This gives us another important lab principle:

> PSP does not replace statistics. Statistics are one of the inputs that allow PSP to work.

Bad, stale, or insufficient statistics may therefore affect whether parameter sensitivity is recognized and how useful the generated variants are.

---

# Experiment 4: Observe the Dispatcher and Query Variants

We should prove PSP behavior rather than infer it from execution times.

Useful Query Store views include:

```sql
sys.query_store_query
sys.query_store_query_text
sys.query_store_plan
sys.query_store_runtime_stats
sys.query_store_query_variant
```

Of particular importance is:

```sql
sys.query_store_query_variant
```

It associates PSP query variants with their parent query and dispatcher plan.

This allows us to answer questions such as:

- Did PSP actually activate?
- Which dispatcher plan was created?
- How many query variants exist?
- Which execution plan belongs to each variant?
- How often was each variant executed?
- How much CPU did each variant consume?
- What was the average duration of each variant?
- How many logical reads did each variant cause?

This is significantly more useful than simply asking:

> Did the query get faster?

---

# Query Store Monitoring Caveat

PSP introduces an important observability concern.

The dispatcher itself does not represent the complete runtime resource consumption of the query.

Executions occur through the query variants.

Therefore, monitoring code written without awareness of PSP can potentially under-report total activity if it examines only the parent query or dispatcher.

The monitoring model should therefore be:

```text
Parent query
     │
     ▼
Dispatcher
     │
 ┌───┼─────────┐
 │   │         │
 ▼   ▼         ▼
V1   V2        V3
 │   │         │
 └───┴────┬────┘
          ▼
Aggregate runtime/resource usage
```

This is an excellent example of why our architecture lab should capture **behavior and resource utilization**, not just elapsed time.

---

# Telemetry to Capture

For each experiment, capture at least the following.

| Metric | Why it matters |
| --- | --- |
| Parameter value | Defines the workload population |
| Compiled parameter value | Shows what informed optimization |
| Runtime parameter value | Shows what actually executed |
| Estimated rows | Optimizer's cardinality expectation |
| Actual rows | Actual workload |
| Estimate/actual ratio | Cardinality-estimation accuracy |
| Physical operators | Seek, scan, lookup, joins, etc. |
| Logical reads | Buffer-pool/database work |
| CPU time | Compute consumed |
| Elapsed time | User-visible latency |
| Memory grant | Memory reserved by the plan |
| Memory used | Actual memory consumption |
| Execution count | Frequency of the workload |
| Compile/recompile count | Compilation cost and stability |
| Query ID | Query Store identity |
| Plan ID | Execution-plan identity |
| Query Variant ID | PSP variant identity |
| Dispatcher Plan ID | PSP parent dispatcher |
| Wait statistics | Resource limiting execution |
| Query Store history | Behavior across time |

The goal is to establish the causal chain:

```text
Data distribution
       ↓
Statistics
       ↓
Cardinality estimation
       ↓
Parameter sensitivity
       ↓
Plan selection
       ↓
Physical operators
       ↓
CPU / I/O / memory / waits
       ↓
Elapsed time
```

---

# Experiment 5: PSP vs. OPTION (RECOMPILE)

PSP and `OPTION (RECOMPILE)` solve related problems using very different strategies.

## OPTION (RECOMPILE)

Conceptually:

```text
Parameter arrives
      ↓
Compile specifically for this value
      ↓
Execute
      ↓
Discard/recompile next time
```

The philosophy is:

> Optimize specifically for this execution rather than maximizing plan reuse.

This can provide excellent specialization but increases compilation CPU and removes much of the benefit of reusable cached plans.

---

## PSP

PSP instead behaves approximately like:

```text
Parameter arrives
      ↓
Dispatcher evaluates cardinality range
      ↓
Select reusable query variant
      ↓
Execute cached specialized plan
```

Its philosophy is:

> Preserve plan reuse while acknowledging that several reusable plans may be required.

This creates an interesting systems-engineering trade-off:

```text
              Specialization
                    ▲
                    │
OPTION RECOMPILE ───┤
                    │
PSP ────────────────────── Reuse
```

Neither mechanism should automatically be considered universally superior.

The correct choice depends on workload characteristics, execution frequency, compilation cost, skew, and operational behavior.

---

# Experiment 6: Disable Parameter Sniffing

Test behavior using:

```sql
USE HINT('DISABLE_PARAMETER_SNIFFING')
```

or an equivalent database-level configuration.

This gives us another useful comparison.

Rather than specializing based on the incoming parameter value, the optimizer must construct a more generic estimate.

Compare:

```text
Normal sniffing
PSP
DISABLE_PARAMETER_SNIFFING
OPTION (RECOMPILE)
```

Measure not only average latency but performance across the different parameter populations.

A generic plan might produce acceptable average performance while being optimal for neither population.

That can still be the correct engineering decision if predictability matters more than peak performance.

---

# Experiment 7: Statistics Degradation

Repeat the PSP workload after deliberately changing the underlying distribution or allowing statistics to become stale.

Questions to investigate:

- Does PSP still recognize the skew?
- Do the bucket boundaries remain reasonable?
- Does the dispatcher continue routing appropriately?
- Do cardinality estimates deteriorate?
- When does statistics maintenance change the resulting plans?

This helps demonstrate the dependency:

```text
Good PSP behavior
      ↑
Cardinality estimation
      ↑
Statistics quality
```

---

# Experiment 8: Missing or Poor Index

PSP should not be treated as a general query-performance optimizer.

Remove or alter the useful `CustomerID` index and rerun the workload.

PSP may correctly recognize multiple cardinality populations while still having poor physical access paths available.

This separates two different concerns:

```text
Which plan should SQL Server choose?
```

from:

```text
Does SQL Server have good physical structures from which to build that plan?
```

---

# Experiment 9: Non-SARGable Predicate

Modify the query so that the predicate becomes non-SARGable.

For example, introduce a function or expression that prevents an efficient index access path.

The purpose is to demonstrate:

> PSP cannot compensate for a fundamentally poor predicate or access strategy.

PSP solves a specific plan-selection problem.

It does not solve every SQL performance problem.

---

# What PSP Does Not Fix

PSP should not be interpreted as "parameter sniffing is solved."

It does not automatically fix:

- non-SARGable predicates
- missing indexes
- inappropriate indexes
- implicit conversions
- stale or misleading statistics
- poor schema design
- blocking
- lock contention
- excessive memory consumption
- bad joins
- intrinsically expensive queries
- poorly designed application access patterns

The important diagnostic question is therefore not:

> Does SQL Server support PSP?

It is:

> Is this query suffering because different parameter cardinalities legitimately require different execution plans?

If the answer is no, PSP is probably not the relevant solution.

---

# Compatibility Level as an Architecture Concern

A particularly useful migration experiment is:

```text
SQL Server 2022 engine
        +
Compatibility level 150
        =
No PSP
```

versus:

```text
SQL Server 2022 engine
        +
Compatibility level 160
        =
PSP eligible
```

This highlights an important SQL Server modernization principle:

> Upgrading the database engine and adopting new optimizer behavior are related but separate decisions.

A database migrated from an older SQL Server version may continue operating at an older compatibility level.

Consequently, simply installing SQL Server 2022 does not mean that all SQL Server 2022 query-processing features are active for a migrated database.

This is especially relevant when evaluating legacy SQL Server modernization projects.

Compatibility-level changes should therefore be treated as testable architectural changes rather than administrative housekeeping.

---

# Proposed Experiment Matrix

| Experiment | Compatibility / configuration | Purpose |
| --- | --- | --- |
| A | Compat 150, small → large | Demonstrate bad small-plan reuse |
| B | Compat 150, large → small | Demonstrate bad large-plan reuse |
| C | Compat 160 + PSP | Observe dispatcher and variants |
| D | Compat 160 + stale statistics | Test PSP dependency on statistics |
| E | Compat 160 + `OPTION (RECOMPILE)` | Compare specialization vs. reuse |
| F | Compat 160 + sniffing disabled | Examine generic-plan behavior |
| G | Compat 160 + poor/missing index | Show what PSP cannot repair |
| H | Compat 160 + non-SARGable predicate | Separate plan sensitivity from query design |
| I | Compat 150 vs. 160 | Demonstrate compatibility-level effects |
| J | PSP + Query Store telemetry | Measure per-variant resource consumption |

---

# Architectural Lessons

## 1. Parameter sniffing is usually useful

Parameter sniffing allows SQL Server to optimize using information it actually has.

The problem is not sniffing itself.

The problem occurs when:

```text
One cached plan
```

is expected to serve:

```text
Multiple radically different workloads
```

---

## 2. Query performance begins with data shape

The same SQL statement can represent fundamentally different workloads depending on the parameter value.

For example:

```text
Customer A → 100 rows
Customer B → 400,000 rows
```

The SQL text is identical.

The physical problem is not.

---

## 3. Cardinality estimation drives architecture

Many downstream optimizer decisions depend upon estimated cardinality:

```text
Estimated rows
      ↓
Join strategy
      ↓
Access path
      ↓
Memory grant
      ↓
Parallelism decisions
      ↓
CPU / I/O / memory
```

Therefore, estimated versus actual cardinality should be a first-class metric in the architecture lab.

---

## 4. Optimization is multidimensional

A query should not be judged solely by elapsed time.

A meaningful comparison includes:

```text
Latency
CPU
Logical reads
Physical reads
Memory
Waits
Concurrency effects
Plan stability
Compilation cost
```

A query that is slightly faster but consumes dramatically more CPU or memory may be a worse system-level solution.

---

## 5. Plan reuse is itself a trade-off

Caching execution plans avoids compilation work.

But reuse becomes harmful when the workload being reused is insufficiently homogeneous.

PSP represents a compromise:

```text
One plan per execution             One plan for everything
       ▲                                      ▲
       │                                      │
OPTION RECOMPILE          PSP          Traditional reuse
       │                   │                  │
Maximum specialization    │          Maximum reuse
                           │
                    Bounded specialization
                    + bounded reuse
```

This is a useful example of a broader architecture principle:

> Optimization often means finding the appropriate boundary between specialization and reuse.

---

# Recommended Lab Philosophy

The lab should avoid conclusions such as:

```text
Query A = 830 ms
Query B = 1.2 seconds
Therefore Query A wins.
```

Instead, each experiment should attempt to explain:

> Why did SQL Server choose this physical strategy?

> What information informed that decision?

> What cardinality did SQL Server expect?

> What cardinality actually occurred?

> Which execution plan was selected?

> What CPU, I/O, memory, and waits resulted from that choice?

> Did that behavior remain stable across different data distributions and parameter values?

The desired progression is:

```text
Workload
   ↓
Data shape
   ↓
Statistics
   ↓
Cardinality estimation
   ↓
Optimizer decision
   ↓
Execution plan
   ↓
Resource consumption
   ↓
Observed performance
```

That makes the project an **architecture and query-processing lab**, rather than merely a SQL benchmarking exercise.

---

# Relevant Reading

## Primary Article

**Parameter Sensitive Plan Optimization vs. Parameter Sniffing: What SQL Server Fixes and What It Doesn't**

SQL Server Central, September 4, 2026.

Excellent practical walkthrough containing the skewed `Orders` workload that inspired this experiment.

https://www.sqlservercentral.com/articles/parameter-sensitive-plan-optimization-vs-parameter-sniffing-what-sql-server-fixes-and-what-it-doesnt

---

## Microsoft: Parameter Sensitive Plan Optimization

**Parameter Sensitive Plan optimization — Microsoft Learn**

Canonical documentation covering:

- PSP eligibility
- dispatcher plans
- query variants
- cardinality ranges
- statistics involvement
- compatibility-level requirements
- PSP configuration and disabling

https://learn.microsoft.com/en-us/sql/relational-databases/performance/parameter-sensitive-plan-optimization

PSP applies to SQL Server 2022 and later and requires database compatibility level 160 for SQL Server 2022 behavior. Microsoft describes it as allowing multiple active cached plans for a single parameterized statement whose optimal plan depends upon data size.

---

## Microsoft: Query Store Query Variants

**sys.query_store_query_variant**

Particularly important for our telemetry work.

https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-query-store-query-variant

Microsoft specifically notes that multiple query variants contribute to the resource usage of the parent query while the dispatcher itself does not generate Query Store runtime statistics. Existing Query Store monitoring therefore needs to account for the variants explicitly.

---

## Microsoft: Query Store Runtime Statistics

**sys.query_store_runtime_stats**

https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-query-store-runtime-stats-transact-sql

Contains aggregated runtime execution statistics associated with Query Store plans and will be one of the core sources for comparing variant behavior.

---

## Microsoft: How Query Store Collects Data

**How Query Store collects data**

https://learn.microsoft.com/en-us/sql/relational-databases/performance/how-query-store-collects-data

Useful background for understanding the relationship among compilation, execution plans, runtime statistics, recompilation, and Query Store persistence.

---

## Microsoft: Database Scoped Configuration

**ALTER DATABASE SCOPED CONFIGURATION**

https://learn.microsoft.com/en-us/sql/t-sql/statements/alter-database-scoped-configuration-transact-sql

Relevant settings include:

```sql
PARAMETER_SENSITIVE_PLAN_OPTIMIZATION
PARAMETER_SNIFFING
```

Microsoft documents PSP as enabled by default beginning with compatibility level 160 and also documents database-level control of parameter sniffing.

---

# Initial Lab Goal

The first milestone does not need to exhaust every PSP edge case.

A useful first iteration would be:

```text
1. Generate intentionally skewed data.

2. Establish useful indexes and statistics.

3. Run compatibility level 150.

4. Compile small → execute large.

5. Compile large → execute small.

6. Capture plans and resource metrics.

7. Switch to compatibility level 160.

8. Observe the PSP dispatcher.

9. Identify query variants through Query Store.

10. Compare variant plans and resource consumption.

11. Add RECOMPILE and disabled-sniffing baselines.

12. Record conclusions.
```

Once this is reproducible, additional experiments involving statistics, indexing, SARGability, concurrency, waits, and memory can build naturally on the same workload.

---

## Bottom Line

The most interesting lesson from PSP is not simply:

> SQL Server 2022 fixed parameter sniffing.

It did not.

The more useful lesson is:

> SQL Server now has a mechanism for recognizing that a single parameterized query may represent several meaningfully different cardinality populations, each of which may deserve its own reusable physical execution strategy.

That makes PSP an excellent vehicle for studying the much larger relationship among:

```text
Data distribution
Statistics
Cardinality estimation
Plan compilation
Plan caching
Plan reuse
Physical operators
Query Store
CPU
I/O
Memory
Waits
Latency
```

Those relationships are exactly what this SQL Server architecture lab should make visible.