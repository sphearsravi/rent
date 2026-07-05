namespace RentCollection.E2ETests;

/// <summary>
/// Groups the end-to-end workflow tests into a single, NON-parallel collection.
/// They each trigger global monthly-invoice generation and rely on per-tenant
/// payment-eligibility ordering, so running them concurrently causes cross-test
/// interference. This collection serializes them (and keeps them off the parallel
/// lane relative to the rest of the suite).
/// </summary>
[CollectionDefinition("WorkflowSerial", DisableParallelization = true)]
public sealed class WorkflowSerialCollection { }
